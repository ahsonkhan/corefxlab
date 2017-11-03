// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Http.Parser.Internal;

namespace System.Text.Http.Parser
{
    public class HttpParser : IHttpParser
    {
        // TODO: commented out;
        //public ILogger Log { get; set; }

        // byte types don't have a data type annotation so we pre-cast them; to avoid in-place casts
        private const byte ByteCR = (byte)'\r';
        private const byte ByteLF = (byte)'\n';
        private const byte ByteColon = (byte)':';
        private const byte ByteSpace = (byte)' ';
        private const byte ByteTab = (byte)'\t';
        private const byte ByteQuestionMark = (byte)'?';
        private const byte BytePercentage = (byte)'%';

        private readonly bool _showErrorDetails;

        public HttpParser()
            : this(showErrorDetails: true)
        {
        }

        public HttpParser(bool showErrorDetails)
        {
            _showErrorDetails = showErrorDetails;
        }

        public bool ParseRequestLine<T>(T handler, in ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined) where T : IHttpRequestLineHandler
        {
            consumed = buffer.Start;
            examined = buffer.End;

            // Prepare the first span
            var span = buffer.First.Span;
            var lineIndex = span.IndexOf(ByteLF);
            if (lineIndex >= 0)
            {
                consumed = buffer.Move(consumed, lineIndex + 1);
                span = span.Slice(0, lineIndex + 1);
            }
            else if (buffer.IsSingleSpan)
            {
                return false;
            }
            else
            {
                if (TryGetNewLineSpan(buffer, out ReadCursor found))
                {
                    span = buffer.Slice(consumed, found).ToSpan();
                    consumed = found;
                }
                else
                {
                    // No request line end
                    return false;
                }
            }

            ParseRequestLine(handler, span);

            examined = consumed;
            return true;
        }

        static readonly byte[] s_Eol = Encoding.UTF8.GetBytes("\r\n");
        public bool ParseRequestLine<T>(ref T handler, in ReadOnlyBytes buffer, out int consumed) where T : IHttpRequestLineHandler
        {
            // Prepare the first span
            var span = buffer.First.Span;
            var lineIndex = span.IndexOf(ByteLF);
            if (lineIndex >= 0)
            {
                consumed = lineIndex + 1;
                span = span.Slice(0, lineIndex + 1);
            }
            else
            {
                var eol = buffer.IndexOf(s_Eol);
                if (eol == -1)
                {
                    consumed = 0;
                    return false;
                }

                span = buffer.Slice(0, eol + s_Eol.Length).ToSpan();
            }

            ParseRequestLine(handler, span);

            consumed = span.Length;
            return true;
        }

        private void ParseRequestLine<T>(T handler, ReadOnlySpan<byte> data) where T : IHttpRequestLineHandler
        {
            int length = data.Length;
            // Get Method and set the offset
            var method = HttpUtilities.GetKnownMethod(data, out var offset);

            ReadOnlySpan<byte> customMethod =
                method == Http.Method.Custom ?
                    GetUnknownMethod(data, out offset) :
                    default;

            // Skip space
            offset++;

            byte ch = 0;
            // Target = Path and Query
            var pathEncoded = false;
            var pathStart = -1;
            for (; offset < length; offset++)
            {
                ch = data[offset];
                if (ch == ByteSpace)
                {
                    if (pathStart == -1)
                    {
                        // Empty path is illegal
                        RejectRequestLine(data);
                    }

                    break;
                }
                else if (ch == ByteQuestionMark)
                {
                    if (pathStart == -1)
                    {
                        // Empty path is illegal
                        RejectRequestLine(data);
                    }

                    break;
                }
                else if (ch == BytePercentage)
                {
                    if (pathStart == -1)
                    {
                        // Path starting with % is illegal
                        RejectRequestLine(data);
                    }

                    pathEncoded = true;
                }
                else if (pathStart == -1)
                {
                    pathStart = offset;
                }
            }

            if (pathStart == -1)
            {
                // Start of path not found
                RejectRequestLine(data);
            }

            ReadOnlySpan<byte> pathBuffer = data.Slice(pathStart, offset - pathStart);

            // Query string
            var queryStart = offset;
            if (ch == ByteQuestionMark)
            {
                // We have a query string
                for (; offset < length; offset++)
                {
                    ch = data[offset];
                    if (ch == ByteSpace)
                    {
                        break;
                    }
                }
            }

            // End of query string not found
            if (offset == length)
            {
                RejectRequestLine(data);
            }

            ReadOnlySpan<byte> targetBuffer = data.Slice(pathStart, offset - pathStart);
            ReadOnlySpan<byte> query = data.Slice(queryStart, offset - queryStart);

            // Consume space
            offset++;

            // Version
            var httpVersion = HttpUtilities.GetKnownVersion(data.Slice(offset, length - offset));
            if (httpVersion == Http.Version.Unknown)
            {
                if (data[offset] == ByteCR || data[length - 2] != ByteCR)
                {
                    // If missing delimiter or CR before LF, reject and log entire line
                    RejectRequestLine(data);
                }
                else
                {
                    // else inform HTTP version is unsupported.
                    RejectUnknownVersion(data.Slice(offset, length - offset - 2));
                }
            }

            // After version's 8 bytes and CR, expect LF
            if (data[offset + 8 + 1] != ByteLF)
            {
                RejectRequestLine(data);
            }

            ReadOnlySpan<byte> line = data;

            handler.OnStartLine(method, httpVersion, targetBuffer, pathBuffer, query, customMethod, pathEncoded);
        }

        public bool ParseHeaders<T>(T handler, in ReadableBuffer buffer, out ReadCursor consumed, out ReadCursor examined, out int consumedBytes) where T : IHttpHeadersHandler
        {
            consumed = buffer.Start;
            examined = buffer.End;
            consumedBytes = 0;

            var bufferEnd = buffer.End;

            var reader = new ReadableBufferReader(buffer);
            var start = default(ReadableBufferReader);
            var done = false;

            try
            {
                while (!reader.End)
                {
                    var span = reader.Span;
                    var remaining = span.Length - reader.Index;

                    while (remaining > 0)
                    {
                        var index = reader.Index;
                        int ch1;
                        int ch2;

                        // Fast path, we're still looking at the same span
                        if (remaining >= 2)
                        {
                            ch1 = span[index];
                            ch2 = span[index + 1];
                        }
                        else
                        {
                            // Store the reader before we look ahead 2 bytes (probably straddling
                            // spans)
                            start = reader;

                            // Possibly split across spans
                            ch1 = reader.Take();
                            ch2 = reader.Take();
                        }

                        if (ch1 == ByteCR)
                        {
                            // Check for final CRLF.
                            if (ch2 == -1)
                            {
                                // Reset the reader so we don't consume anything
                                reader = start;
                                return false;
                            }
                            else if (ch2 == ByteLF)
                            {
                                // If we got 2 bytes from the span directly so skip ahead 2 so that
                                // the reader's state matches what we expect
                                if (index == reader.Index)
                                {
                                    reader.Skip(2);
                                }

                                done = true;
                                return true;
                            }

                            // Headers don't end in CRLF line.
                            RejectRequest(RequestRejectionReason.InvalidRequestHeadersNoCRLF);
                        }

                        // We moved the reader so look ahead 2 bytes so reset both the reader
                        // and the index
                        if (index != reader.Index)
                        {
                            reader = start;
                            index = reader.Index;
                        }

                        var endIndex = span.Slice(index, remaining).IndexOf(ByteLF);
                        var length = 0;

                        if (endIndex != -1)
                        {
                            length = endIndex + 1;
                            var pHeader = span.Slice(index);

                            TakeSingleHeader(pHeader, length, handler);
                        }
                        else
                        {
                            var current = reader.Cursor;

                            // Split buffers
                            if (ReadCursorOperations.Seek(current, bufferEnd, out var lineEnd, ByteLF) == -1)
                            {
                                // Not there
                                return false;
                            }

                            // Make sure LF is included in lineEnd
                            lineEnd = buffer.Move(lineEnd, 1);
                            var headerSpan = buffer.Slice(current, lineEnd).ToSpan();
                            length = headerSpan.Length;

                            TakeSingleHeader(headerSpan, length, handler);

                            // We're going to the next span after this since we know we crossed spans here
                            // so mark the remaining as equal to the headerSpan so that we end up at 0
                            // on the next iteration
                            remaining = length;
                        }

                        // Skip the reader forward past the header line
                        reader.Skip(length);
                        remaining -= length;
                    }
                }

                return false;
            }
            finally
            {
                consumed = reader.Cursor;
                consumedBytes = reader.ConsumedBytes;

                if (done)
                {
                    examined = consumed;
                }
            }
        }

        public bool ParseHeaders<T>(T handler, ReadOnlyBytes buffer, out int consumedBytes) where T : class, IHttpHeadersHandler
        {
            return ParseHeaders(ref handler, buffer, out consumedBytes);
        }

        public bool ParseHeaders<T>(ref T handler, ReadOnlyBytes buffer, out int consumedBytes) where T : IHttpHeadersHandler
        {
            var index = 0;        
            var rest = buffer.Rest;
            var span = buffer.First.Span;
            var remaining = span.Length;
            consumedBytes = 0;
            while (true) {
                while (remaining > 0) {

                    int ch1;
                    int ch2;

                    // Fast path, we're still looking at the same span
                    if (remaining >= 2) {
                        ch1 = span[index];
                        ch2 = span[index + 1];
                    }
                    // Slow Path
                    else {
                        ReadNextTwoChars(span, remaining, index, out ch1, out ch2, rest);
                    }

                    if (ch1 == ByteCR) {
                        if (ch2 == ByteLF) {
                            consumedBytes += 2;
                            return true;
                        }

                        if (ch2 == -1) {
                            consumedBytes = 0;
                            return false;
                        }

                        // Headers don't end in CRLF line.
                        RejectRequest(RequestRejectionReason.InvalidRequestHeadersNoCRLF);
                    }

                    var endIndex = span.Slice(index, remaining).IndexOf(ByteLF);
                    var length = 0;

                    if (endIndex != -1) {
                        length = endIndex + 1;
                        var pHeader = span.Slice(index);

                        TakeSingleHeader(pHeader, length, handler);
                    }
                    else {
                        // Split buffers
                        var end = buffer.Slice(index).IndexOf(ByteLF);
                        if (end == -1) {
                            // Not there
                            consumedBytes = 0;
                            return false;
                        }

                        var headerSpan = buffer.Slice(index, end - index + 1).ToSpan();
                        length = headerSpan.Length;
                        
                        TakeSingleHeader(headerSpan, length, handler);
                    }

                    // Skip the reader forward past the header line
                    index += length;
                    consumedBytes += length;
                    remaining -= length;
                }

                if (rest != null) {
                    span = rest.First.Span;
                    rest = rest.Rest;
                    remaining = span.Length + remaining;
                    index = span.Length - remaining;
                }
                else {
                    consumedBytes = 0;
                    return false;
                }
            }
        }

        private static void ReadNextTwoChars(ReadOnlySpan<byte> pBuffer, int remaining, int index, out int ch1, out int ch2, IReadOnlyBufferList<byte> rest)
        {
            if (remaining == 1) {
                ch1 = pBuffer[index];
                if (rest == null) {
                    ch2 = -1;
                }
                else {
                    ch2 = rest.First.Span[0];
                }
            }
            else {
                ch1 = -1;
                ch2 = -1;
                if (rest != null) {
                    var nextSpan = rest.First.Span;
                    if (nextSpan.Length > 0) {
                        ch1 = nextSpan[0];

                        if (nextSpan.Length > 1) {
                            ch2 = nextSpan[1];
                        }
                        else {
                            ch2 = -1;
                        }
                    }
                    else {
                        ch2 = -1;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindEndOfName(ReadOnlySpan<byte> headerLine)
        {
            int length = headerLine.Length;
            var index = 0;
            var sawWhitespace = false;
            for (; index < length; index++)
            {
                var ch = headerLine[index];
                if (ch == ByteColon)
                {
                    break;
                }
                if (ch == ByteTab || ch == ByteSpace || ch == ByteCR)
                {
                    sawWhitespace = true;
                }
            }

            if (index == length || sawWhitespace)
            {
                RejectRequestHeader(headerLine);
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TakeSingleHeader<T>(ReadOnlySpan<byte> headerLine, int length, T handler) where T : IHttpHeadersHandler
        {
            // Skip CR, LF from end position
            var valueEnd = length - 3;
            var nameEnd = FindEndOfName(headerLine);

            if (headerLine[valueEnd + 2] != ByteLF)
            {
                RejectRequestHeader(headerLine);
            }
            if (headerLine[valueEnd + 1] != ByteCR)
            {
                RejectRequestHeader(headerLine);
            }

            // Skip colon from value start
            var valueStart = nameEnd + 1;
            // Ignore start whitespace
            for (; valueStart < valueEnd; valueStart++)
            {
                var ch = headerLine[valueStart];
                if (ch != ByteTab && ch != ByteSpace && ch != ByteCR)
                {
                    break;
                }
                else if (ch == ByteCR)
                {
                    RejectRequestHeader(headerLine);
                }
            }

            // Check for CR in value
            var i = valueStart + 1;
            if (Contains(headerLine.Slice(i), valueEnd - i, ByteCR))
            {
                RejectRequestHeader(headerLine);
            }

            // Ignore end whitespace
            for (; valueEnd >= valueStart; valueEnd--)
            {
                var ch = headerLine[valueEnd];
                if (ch != ByteTab && ch != ByteSpace)
                {
                    break;
                }
            }

            ReadOnlySpan<byte> nameBuffer = headerLine.Slice(0, nameEnd);
            ReadOnlySpan<byte> valueBuffer = headerLine.Slice(valueStart, valueEnd - valueStart + 1);

            handler.OnHeader(nameBuffer, valueBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe bool Contains(ReadOnlySpan<byte> data, int length, byte value)
        {
            fixed (byte* searchSpace = &data.DangerousGetPinnableReference())
            {
                var i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    // Check Vector lengths
                    if (length - Vector<byte>.Count >= i)
                    {
                        var vValue = GetVector(value);
                        do
                        {
                            if (!Vector<byte>.Zero.Equals(Vector.Equals(vValue, Unsafe.Read<Vector<byte>>(searchSpace + i))))
                            {
                                goto found;
                            }

                            i += Vector<byte>.Count;
                        } while (length - Vector<byte>.Count >= i);
                    }
                }

                // Check remaining for CR
                for (; i <= length; i++)
                {
                    var ch = searchSpace[i];
                    if (ch == value)
                    {
                        goto found;
                    }
                }
                return false;
            found:
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryGetNewLineSpan(in ReadableBuffer buffer, out ReadCursor found)
        {
            var start = buffer.Start;
            if (ReadCursorOperations.Seek(start, buffer.End, out found, ByteLF) != -1)
            {
                // Move 1 byte past the \n
                found = buffer.Move(found, 1);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private ReadOnlySpan<byte> GetUnknownMethod(ReadOnlySpan<byte> data, out int methodLength)
        {
            int length = data.Length;
            methodLength = 0;
            for (var i = 0; i < length; i++)
            {
                var ch = data[i];

                if (ch == ByteSpace)
                {
                    if (i == 0)
                    {
                        RejectRequestLine(data);
                    }

                    methodLength = i;
                    break;
                }
                else if (!IsValidTokenChar((char)ch))
                {
                    RejectRequestLine(data);
                }
            }

            return data.Slice(0, methodLength);
        }

        // TODO: this could be optimized by using a table
        private static bool IsValidTokenChar(char c)
        {
            // Determines if a character is valid as a 'token' as defined in the
            // HTTP spec: https://tools.ietf.org/html/rfc7230#section-3.2.6
            return
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                c == '!' ||
                c == '#' ||
                c == '$' ||
                c == '%' ||
                c == '&' ||
                c == '\'' ||
                c == '*' ||
                c == '+' ||
                c == '-' ||
                c == '.' ||
                c == '^' ||
                c == '_' ||
                c == '`' ||
                c == '|' ||
                c == '~';
        }

        private void RejectRequest(RequestRejectionReason reason)
            => throw BadHttpRequestException.GetException(reason);

        private void RejectRequestLine(ReadOnlySpan<byte> requestLine)
            => throw GetInvalidRequestException(RequestRejectionReason.InvalidRequestLine, requestLine);

        private void RejectRequestHeader(ReadOnlySpan<byte> headerLine)
            => throw GetInvalidRequestException(RequestRejectionReason.InvalidRequestHeader, headerLine);

        private void RejectUnknownVersion(ReadOnlySpan<byte> version)
            => throw GetInvalidRequestException(RequestRejectionReason.UnrecognizedHTTPVersion, version);

        private BadHttpRequestException GetInvalidRequestException(RequestRejectionReason reason, ReadOnlySpan<byte> detail)
        {
            string errorDetails = _showErrorDetails ?
                detail.GetAsciiStringEscaped(Constants.MaxExceptionDetailSize) :
                string.Empty;
            return BadHttpRequestException.GetException(reason, errorDetails);
        }

        public void Reset()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector<byte> GetVector(byte vectorByte)
        {
            // Vector<byte> .ctor doesn't become an intrinsic due to detection issue
            // However this does cause it to become an intrinsic (with additional multiply and reg->reg copy)
            // https://github.com/dotnet/coreclr/issues/7459#issuecomment-253965670
            return Vector.AsVectorByte(new Vector<uint>(vectorByte * 0x01010101u));
        }

        enum State : byte
        {
            ParsingName,
            BeforValue,
            ParsingValue,
            ArferCR,
        }
    }
}
