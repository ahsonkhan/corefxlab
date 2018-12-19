﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text.JsonLab
{
    public static partial class Utf8JsonWriterHelpers
    {
        // TODO: Either implement the escaping helpers from scratch or leverage the upcoming System.Text.Encodings.Web.TextEncoder APIs
        public static OperationStatus EscapeString(string value, Span<byte> destination, out int consumed, out int bytesWritten)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteDigits(uint value, Span<byte> buffer)
        {
            // We can mutate the 'value' parameter since it's a copy-by-value local.
            // It'll be used to represent the value left over after each division by 10.

            for (int i = buffer.Length - 1; i >= 1; i--)
            {
                uint temp = '0' + value;
                value /= 10;
                buffer[i] = (byte)(temp - (value * 10));
            }

            Debug.Assert(value < 10);
            buffer[0] = (byte)('0' + value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteDigits(uint value, Span<char> buffer)
        {
            // We can mutate the 'value' parameter since it's a copy-by-value local.
            // It'll be used to represent the value left over after each division by 10.

            for (int i = buffer.Length - 1; i >= 1; i--)
            {
                uint temp = '0' + value;
                value /= 10;
                buffer[i] = (char)(temp - (value * 10));
            }

            Debug.Assert(value < 10);
            buffer[0] = (char)('0' + value);
        }

        internal static void EscapeStringInternal(ref ReadOnlySpan<byte> value, ref Span<byte> destination, int indexOfFirstByteToEscape, out int bytesWritten)
        {
            Debug.Assert(indexOfFirstByteToEscape >= 0 && indexOfFirstByteToEscape < value.Length);

            value.Slice(0, indexOfFirstByteToEscape).CopyTo(destination);
            bytesWritten = indexOfFirstByteToEscape;
            int consumed = indexOfFirstByteToEscape;

            while (consumed < value.Length)
            {
                byte val = value[consumed];
                if (NeedsEscaping(val))
                {
                    destination[bytesWritten++] = (byte)'\\';
                    destination[bytesWritten++] = (byte)'u';
                    SequenceValidity status = Utf8Utility.PeekFirstSequence(value.Slice(consumed), out int numBytesConsumed, out UnicodeScalar scalarValue);
                    if (status != SequenceValidity.WellFormed)
                    {
                        JsonThrowHelper.ThrowJsonWriterException("Invalid UTF-8 string.");
                    }
                    WriteDigits((uint)scalarValue.Value, destination.Slice(bytesWritten, 4));
                    bytesWritten += 4;
                    consumed += numBytesConsumed;
                }
                else
                {
                    destination[bytesWritten] = value[consumed];
                    bytesWritten++;
                    consumed++;
                }
            }
        }

        internal static void EscapeStringInternal(ref ReadOnlySpan<char> value, ref Span<char> destination, int indexOfFirstByteToEscape, out int charsWritten)
        {
            Debug.Assert(indexOfFirstByteToEscape >= 0 && indexOfFirstByteToEscape < value.Length);

            value.Slice(0, indexOfFirstByteToEscape).CopyTo(destination);
            charsWritten = indexOfFirstByteToEscape;
            int consumed = indexOfFirstByteToEscape;

            while (consumed < value.Length)
            {
                char val = value[consumed];
                if (NeedsEscaping(val))
                {
                    destination[charsWritten++] = '\\';
                    destination[charsWritten++] = 'u';

                    int scalar = val;
                    consumed++;
                    if (InRange(scalar, 0xD800, 0xDFFF))
                    {
                        consumed++;
                        if (value.Length <= consumed || scalar >= 0xDC00)
                        {
                            JsonThrowHelper.ThrowJsonWriterException("Invalid UTF-16 string ending in an invalid surrogate pair.");
                        }
                        int highSurrogate = (scalar - 0xD800) * 0x400;
                        int lowSurrogate = value[consumed] - 0xDc00;
                        scalar = highSurrogate + lowSurrogate;
                    }
                    WriteDigits((uint)scalar, destination.Slice(charsWritten, 4));
                    charsWritten += 4;
                }
                else
                {
                    destination[charsWritten] = value[consumed];
                    charsWritten++;
                    consumed++;
                }
            }
        }

        private static bool InRange(int ch, int start, int end)
        {
            return (uint)(ch - start) <= (uint)(end - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int NeedsEscaping(ref ReadOnlySpan<byte> value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                byte val = value[index];
                if (NeedsEscaping(val))
                {
                    return index;
                }
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int NeedsEscaping(ref ReadOnlySpan<char> value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char val = value[index];
                if (NeedsEscaping(val))
                {
                    return index;
                }
            }
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedsEscaping(byte value)
        {
            return !InRange(value, ' ', '~') || JsonConstants.ForbiddenBytes.IndexOf(value) != -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedsEscaping(char value)
        {
            return !InRange(value, ' ', '~') || JsonConstants.ForbiddenChars.IndexOf(value) != -1;
        }

        public static OperationStatus EscapeString(ReadOnlySpan<char> value, Span<byte> destination, out int consumed, out int bytesWritten)
        {
            //    // TODO:
            //    // if destination too small, return
            //    // stack-alloc for small values.
            //    // Check if there is no need to escape and encode directly into destination

            //    byte[] utf8Intermediary = ArrayPool<byte>.Shared.Rent(destination.Length);
            //    Span<byte> utf8Scratch = utf8Intermediary;
            //    OperationStatus status = Buffers.Text.Encodings.Utf16.ToUtf8(MemoryMarshal.AsBytes(value), utf8Scratch, out int encodingConsumed, out int encodingWritten);

            //    if (status != OperationStatus.Done)
            //    {
            //        consumed = 0;
            //        bytesWritten = 0;
            //        goto Done;
            //    }

            //    utf8Scratch = utf8Scratch.Slice(0, encodingWritten);

            //    // TODO: Divide by 2?
            //    consumed = encodingConsumed;
            //    bytesWritten = encodingWritten;

            //    if (JsonWriterHelper.IndexOfAnyEscape(utf8Scratch) != -1)
            //    {
            //        for (int i = 0; i < utf8Scratch.Length; i++)
            //        {
            //            //TODO: Escape all but the white-listed bytes and copy into the destination.
            //            if (i > destination.Length)
            //            {
            //                status = OperationStatus.DestinationTooSmall;
            //                goto Done;
            //            }
            //        }
            //        status = OperationStatus.Done;
            //    }
            //    else
            //    {
            //        if (utf8Scratch.Length <= destination.Length)
            //        {
            //            utf8Scratch.CopyTo(destination);
            //            bytesWritten = utf8Scratch.Length;
            //            status = OperationStatus.Done;
            //        }
            //        else
            //        {
            //            utf8Scratch.Slice(0, destination.Length).CopyTo(destination);
            //            bytesWritten = destination.Length;
            //            status = OperationStatus.DestinationTooSmall;
            //        }
            //    }

            //Done:
            //    ArrayPool<byte>.Shared.Return(utf8Intermediary);
            //    return status;

            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static OperationStatus EscapeString(ReadOnlySpan<byte> value, Span<byte> destination, out int consumed, out int bytesWritten)
        {
            throw new NotImplementedException();
        }
    }
}