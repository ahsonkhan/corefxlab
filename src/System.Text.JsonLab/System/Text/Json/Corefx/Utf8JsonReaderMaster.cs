﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text.Json.Corefx
{
    /// <summary>
    /// Provides a high-performance API for forward-only, read-only access to the UTF-8 encoded JSON text.
    /// It processes the text sequentially with no caching and adheres strictly to the JSON RFC
    /// by default (https://tools.ietf.org/html/rfc8259). When it encounters invalid JSON, it throws
    /// a JsonReaderException with basic error information like line number and byte position on the line.
    /// Since this type is a ref struct, it does not directly support async. However, it does provide
    /// support for reentrancy to read incomplete data, and continue reading once more data is presented.
    /// To be able to set max depth while reading OR allow skipping comments, create an instance of 
    /// <see cref="JsonReaderStateMaster"/> and pass that in to the reader.
    /// </summary>
    public ref partial struct Utf8JsonReaderMaster
    {
        private ReadOnlySpan<byte> _buffer;

        private bool _isFinalBlock;

        private ulong _stackFreeContainer;
        private long _lineNumber;
        private long _bytePositionInLine;
        private int _consumed;
        private int _currentDepth;
        private int _maxDepth;
        private bool _inObject;
        private bool _isNotPrimitive;
        private JsonTokenType _tokenType;
        private JsonTokenType _previousTokenType;
        private JsonReaderOptions _readerOptions;
        private Stack<JsonTokenType> _stack;

        private long _totalConsumed;
        private bool _isLastSegment;
        private readonly bool _isSingleSegment;

        private bool IsLastSpan => _isFinalBlock && (_isSingleSegment || _isLastSegment);

        /// <summary>
        /// Gets the value of the last processed token as a ReadOnlySpan&lt;byte&gt; slice
        /// of the input payload. If the JSON is provided within a ReadOnlySequence&lt;byte&gt;
        /// and the slice that represents the token value fits in a single segment, then
        /// ValueSpan will contain the sliced value since it can be represented as a span.
        /// </summary>
        public ReadOnlySpan<byte> ValueSpan { get; private set; }

        /// <summary>
        /// Returns the total amount of bytes consumed by the <see cref="Utf8JsonReader"/> so far
        /// for the current instance of the <see cref="Utf8JsonReader"/> with the given UTF-8 encoded input text.
        /// </summary>
        public long BytesConsumed => _totalConsumed + _consumed;

        /// <summary>
        /// Tracks the recursive depth of the nested objects / arrays within the JSON text
        /// processed so far. This provides the depth of the current token.
        /// </summary>
        public int CurrentDepth => _currentDepth;

        /// <summary>
        /// Gets the type of the last processed JSON token in the UTF-8 encoded JSON text.
        /// </summary>
        public JsonTokenType TokenType => _tokenType;

        /// <summary>
        /// Returns the current snapshot of the <see cref="Utf8JsonReader"/> state which must
        /// be captured by the caller and passed back in to the <see cref="Utf8JsonReader"/> ctor with more data.
        /// Unlike the <see cref="Utf8JsonReader"/>, which is a ref struct, the state can survive
        /// across async/await boundaries and hence this type is required to provide support for reading
        /// in more data asynchronously before continuing with a new instance of the <see cref="Utf8JsonReader"/>.
        /// </summary>
        public JsonReaderStateMaster CurrentState => new JsonReaderStateMaster
        {
            _stackFreeContainer = _stackFreeContainer,
            _lineNumber = _lineNumber,
            _bytePositionInLine = _bytePositionInLine,
            _bytesConsumed = BytesConsumed,
            _currentDepth = _currentDepth,
            _maxDepth = _maxDepth,
            _inObject = _inObject,
            _isNotPrimitive = _isNotPrimitive,
            _tokenType = _tokenType,
            _previousTokenType = _previousTokenType,
            _readerOptions = _readerOptions,
            _stack = _stack,
        };

        /// <summary>
        /// Constructs a new <see cref="Utf8JsonReader"/> instance.
        /// </summary>
        /// <param name="jsonData">The ReadOnlySpan&lt;byte&gt; containing the UTF-8 encoded JSON text to process.</param>
        /// <param name="isFinalBlock">True when the input span contains the entire data to process.
        /// Set to false only if it is known that the input span contains partial data with more data to follow.</param>
        /// <param name="state">If this is the first call to the ctor, pass in a default state. Otherwise,
        /// capture the state from the previous instance of the <see cref="Utf8JsonReader"/> and pass that back.</param>
        /// <remarks>
        /// Since this type is a ref struct, it is a stack-only type and all the limitations of ref structs apply to it.
        /// This is the reason why the ctor accepts a <see cref="JsonReaderStateMaster"/>.
        /// </remarks>
        public Utf8JsonReaderMaster(ReadOnlySpan<byte> jsonData, bool isFinalBlock, JsonReaderStateMaster state)
        {
            _buffer = jsonData;

            _isFinalBlock = isFinalBlock;

            // Note: We do not retain _bytesConsumed or _sequencePosition as they reset with the new input data
            _stackFreeContainer = state._stackFreeContainer;
            _lineNumber = state._lineNumber;
            _bytePositionInLine = state._bytePositionInLine;
            _currentDepth = state._currentDepth;
            _maxDepth = state._maxDepth == 0 ? JsonReaderStateMaster.StackFreeMaxDepth : state._maxDepth; // If max depth is not set, revert to the default depth.
            _inObject = state._inObject;
            _isNotPrimitive = state._isNotPrimitive;
            _tokenType = state._tokenType;
            _previousTokenType = state._previousTokenType;
            _readerOptions = state._readerOptions;
            _stack = state._stack;

            _consumed = 0;
            _totalConsumed = 0;
            _isLastSegment = _isFinalBlock;
            _isSingleSegment = true;

            ValueSpan = ReadOnlySpan<byte>.Empty;
        }

        /// <summary>
        /// Read the next JSON token from input source.
        /// </summary>
        /// <returns>True if the token was read successfully, else false.</returns>
        /// <exception cref="JsonReaderException">
        /// Thrown when an invalid JSON token is encountered according to the JSON RFC
        /// or if the current depth exceeds the recursive limit set by the max depth.
        /// </exception>
        public bool Read() => ReadSingleSegment();

        private void StartObject()
        {
            if (_currentDepth >= _maxDepth)
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ObjectDepthTooLarge);

            _currentDepth++;
            _consumed++;
            _bytePositionInLine++;

            if (_currentDepth <= JsonReaderStateMaster.StackFreeMaxDepth)
            {
                _stackFreeContainer = (_stackFreeContainer << 1) | 1;
            }
            else
            {
                EnsureAndPushStack(JsonTokenType.StartObject);
            }

            _tokenType = JsonTokenType.StartObject;
            _inObject = true;
        }

        private void EndObject()
        {
            if (!_inObject || _currentDepth <= 0)
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.MismatchedObjectArray, JsonConstants.CloseBrace);

            _tokenType = JsonTokenType.EndObject;

            UpdateStackOnEndToken();
        }

        private void StartArray()
        {
            if (_currentDepth >= _maxDepth)
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ArrayDepthTooLarge);

            _currentDepth++;
            _consumed++;
            _bytePositionInLine++;

            if (_currentDepth <= JsonReaderStateMaster.StackFreeMaxDepth)
            {
                _stackFreeContainer = _stackFreeContainer << 1;
            }
            else
            {
                EnsureAndPushStack(JsonTokenType.StartArray);
            }

            _tokenType = JsonTokenType.StartArray;
            _inObject = false;
        }

        private void EndArray()
        {
            if (_inObject || _currentDepth <= 0)
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.MismatchedObjectArray, JsonConstants.CloseBracket);

            _tokenType = JsonTokenType.EndArray;

            UpdateStackOnEndToken();
        }

        // Allocate the stack lazily only when it is absolutely necessary
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void EnsureAndPushStack(JsonTokenType tokenType)
        {
            Debug.Assert(tokenType == JsonTokenType.StartArray || tokenType == JsonTokenType.StartObject);
            if (_stack == null)
            {
                _stack = new Stack<JsonTokenType>();
            }
            _stack.Push(tokenType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateStackOnEndToken()
        {
            _consumed++;
            _bytePositionInLine++;

            if (_currentDepth <= JsonReaderStateMaster.StackFreeMaxDepth)
            {
                _stackFreeContainer >>= 1;
                _inObject = (_stackFreeContainer & 1) != 0;
            }
            else
            {
                Debug.Assert(_stack.Count > 0);
                _stack.Pop();
                if (_stack.Count == 0)
                {
                    _inObject = (_stackFreeContainer & 1) != 0;
                }
                else
                {
                    _inObject = _stack.Peek() != JsonTokenType.StartArray;
                }
            }

            _currentDepth--;
        }

        private bool ReadSingleSegment()
        {
            bool retVal = false;

            if (!HasMoreData())
            {
                goto Done;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            // SkipWhiteSpace only skips the whitespace characters as defined by JSON RFC 8259 section 2.
            // We do not validate if 'first' is an invalid JSON byte here (such as control characters).
            // Those cases are captured in ConsumeNextToken and ConsumeValue.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData())
                {
                    goto Done;
                }
                first = _buffer[_consumed];
            }

            if (_tokenType == JsonTokenType.None)
            {
                goto ReadFirstToken;
            }

            if (first == JsonConstants.Slash)
            {
                retVal = ConsumeNextTokenOrRollback(first);
                goto Done;
            }

            if (_tokenType == JsonTokenType.StartObject)
            {
                if (first == JsonConstants.CloseBrace)
                {
                    EndObject();
                }
                else
                {
                    if (first != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }

                    int prevConsumed = _consumed;
                    long prevPosition = _bytePositionInLine;
                    long prevLineNumber = _lineNumber;
                    retVal = ConsumePropertyName();
                    if (!retVal)
                    {
                        // roll back potential changes
                        _consumed = prevConsumed;
                        _tokenType = JsonTokenType.StartObject;
                        _bytePositionInLine = prevPosition;
                        _lineNumber = prevLineNumber;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {
                if (first == JsonConstants.CloseBracket)
                {
                    EndArray();
                }
                else
                {
                    retVal = ConsumeValue(first);
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                retVal = ConsumeValue(first);
                goto Done;
            }
            else
            {
                retVal = ConsumeNextTokenOrRollback(first);
                goto Done;
            }

            retVal = true;

        Done:
            return retVal;

        ReadFirstToken:
            retVal = ReadFirstToken(first);
            goto Done;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasMoreData()
        {
            if (_consumed >= (uint)_buffer.Length)
            {
                if (_isNotPrimitive && IsLastSpan)
                {
                    if (_currentDepth != 0)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ZeroDepthAtEnd);
                    }

                    if (_readerOptions.CommentHandling == JsonCommentHandling.Allow && _tokenType == JsonTokenType.Comment)
                    {
                        return false;
                    }

                    if (_tokenType != JsonTokenType.EndArray && _tokenType != JsonTokenType.EndObject)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJsonNonPrimitive);
                    }
                }
                return false;
            }
            return true;
        }

        // Unlike the parameter-less overload of HasMoreData, if there is no more data when this method is called, we know the JSON input is invalid.
        // This is because, this method is only called after a ',' (i.e. we expect a value/property name) or after 
        // a property name, which means it must be followed by a value.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasMoreData(ExceptionResource resource)
        {
            if (_consumed >= (uint)_buffer.Length)
            {
                if (IsLastSpan)
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, resource);
                }
                return false;
            }
            return true;
        }

        private bool ReadFirstToken(byte first)
        {
            if (first == JsonConstants.OpenBrace)
            {
                _currentDepth++;
                _stackFreeContainer = 1;
                _tokenType = JsonTokenType.StartObject;
                _consumed++;
                _bytePositionInLine++;
                _inObject = true;
                _isNotPrimitive = true;
            }
            else if (first == JsonConstants.OpenBracket)
            {
                _currentDepth++;
                _tokenType = JsonTokenType.StartArray;
                _consumed++;
                _bytePositionInLine++;
                _isNotPrimitive = true;
            }
            else
            {
                // Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localBuffer = _buffer;

                if (JsonReaderHelper.IsDigit(first) || first == '-')
                {
                    if (!TryGetNumber(localBuffer.Slice(_consumed), out int numberOfBytes))
                    {
                        return false;
                    }
                    _tokenType = JsonTokenType.Number;
                    _consumed += numberOfBytes;
                    _bytePositionInLine += numberOfBytes;
                    goto Done;
                }
                else if (ConsumeValue(first))
                {
                    goto Done;
                }

                return false;

            Done:
                // Cannot use HasMoreData since the JSON payload contains a single, non-primitive value
                // and hence must be handled differently.
                if (_consumed >= (uint)localBuffer.Length)
                {
                    return true;
                }

                if (localBuffer[_consumed] <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    if (_consumed >= (uint)localBuffer.Length)
                    {
                        return true;
                    }
                }

                if (_readerOptions.CommentHandling != JsonCommentHandling.Disallow)
                {
                    if (_readerOptions.CommentHandling == JsonCommentHandling.Allow)
                    {
                        // This is necessary to avoid throwing when the user has 1 or more comments as the first token
                        // OR if there is a comment after a single, non-primitive value.
                        // In this mode, ConsumeValue consumes the comment and we need to return it as a token.
                        // along with future comments in subsequeunt reads.
                        if (_tokenType == JsonTokenType.Comment || localBuffer[_consumed] == JsonConstants.Slash)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        Debug.Assert(_readerOptions.CommentHandling == JsonCommentHandling.Skip);
                        if (_tokenType == JsonTokenType.StartObject || _tokenType == JsonTokenType.StartArray)
                        {
                            _isNotPrimitive = true;
                        }
                        return true;
                    }
                }
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, localBuffer[_consumed]);
            }
            return true;
        }

        private void SkipWhiteSpace()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer;
            for (; _consumed < localBuffer.Length; _consumed++)
            {
                byte val = localBuffer[_consumed];

                // JSON RFC 8259 section 2 says only these 4 characters count, not all of the Unicode defintions of whitespace.
                if (val != JsonConstants.Space &&
                    val != JsonConstants.CarriageReturn &&
                    val != JsonConstants.LineFeed &&
                    val != JsonConstants.Tab)
                {
                    break;
                }

                if (val == JsonConstants.LineFeed)
                {
                    _lineNumber++;
                    _bytePositionInLine = 0;
                }
                else
                {
                    _bytePositionInLine++;
                }
            }
        }

        /// <summary>
        /// This method contains the logic for processing the next value token and determining
        /// what type of data it is.
        /// </summary>
        private bool ConsumeValue(byte marker)
        {
            while (true)
            {
                if (marker == JsonConstants.Quote)
                {
                    return ConsumeString();
                }
                else if (marker == JsonConstants.OpenBrace)
                {
                    StartObject();
                }
                else if (marker == JsonConstants.OpenBracket)
                {
                    StartArray();
                }
                else if (JsonReaderHelper.IsDigit(marker) || marker == '-')
                {
                    return ConsumeNumber();
                }
                else if (marker == 'f')
                {
                    return ConsumeLiteral(JsonConstants.FalseValue, JsonTokenType.False);
                }
                else if (marker == 't')
                {
                    return ConsumeLiteral(JsonConstants.TrueValue, JsonTokenType.True);
                }
                else if (marker == 'n')
                {
                    return ConsumeLiteral(JsonConstants.NullValue, JsonTokenType.Null);
                }
                else
                {
                    switch (_readerOptions.CommentHandling)
                    {
                        case JsonCommentHandling.Disallow:
                            break;
                        case JsonCommentHandling.Allow:
                            if (marker == JsonConstants.Slash)
                            {
                                return ConsumeComment();
                            }
                            break;
                        default:
                            Debug.Assert(_readerOptions.CommentHandling == JsonCommentHandling.Skip);
                            if (marker == JsonConstants.Slash)
                            {
                                if (SkipComment())
                                {
                                    if (_consumed >= (uint)_buffer.Length)
                                    {
                                        if (_isNotPrimitive && IsLastSpan && _tokenType != JsonTokenType.EndArray && _tokenType != JsonTokenType.EndObject)
                                        {
                                            ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJsonNonPrimitive);
                                        }
                                        return false;
                                    }

                                    marker = _buffer[_consumed];

                                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                                    if (marker <= JsonConstants.Space)
                                    {
                                        SkipWhiteSpace();
                                        if (!HasMoreData())
                                        {
                                            return false;
                                        }
                                        marker = _buffer[_consumed];
                                    }

                                    // Skip comments and consume the actual JSON value.
                                    continue;
                                }
                                return false;
                            }
                            break;
                    }
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }
                break;
            }
            return true;
        }

        // Consumes 'null', or 'true', or 'false'
        private bool ConsumeLiteral(ReadOnlySpan<byte> literal, JsonTokenType tokenType)
        {
            ReadOnlySpan<byte> span = _buffer.Slice(_consumed);
            Debug.Assert(span.Length > 0);
            Debug.Assert(span[0] == 'n' || span[0] == 't' || span[0] == 'f');

            if (!span.StartsWith(literal))
            {
                return CheckLiteral(span, literal);
            }

            ValueSpan = span.Slice(0, literal.Length);
            _tokenType = tokenType;
            _consumed += literal.Length;
            _bytePositionInLine += literal.Length;
            return true;
        }

        private bool CheckLiteral(ReadOnlySpan<byte> span, ReadOnlySpan<byte> literal)
        {
            Debug.Assert(span.Length > 0 && span[0] == literal[0]);

            int indexOfFirstMismatch = 0;

            for (int i = 1; i < literal.Length; i++)
            {
                if (span.Length > i)
                {
                    if (span[i] != literal[i])
                    {
                        _bytePositionInLine += i;
                        ThrowInvalidLiteral(span);
                    }
                }
                else
                {
                    indexOfFirstMismatch = i;
                    break;
                }
            }

            Debug.Assert(indexOfFirstMismatch > 0 && indexOfFirstMismatch < literal.Length);

            if (IsLastSpan)
            {
                _bytePositionInLine += indexOfFirstMismatch;
                ThrowInvalidLiteral(span);
            }
            return false;
        }

        private void ThrowInvalidLiteral(ReadOnlySpan<byte> span)
        {
            byte firstByte = span[0];

            ExceptionResource resource;
            switch (firstByte)
            {
                case (byte)'t':
                    resource = ExceptionResource.ExpectedTrue;
                    break;
                case (byte)'f':
                    resource = ExceptionResource.ExpectedFalse;
                    break;
                default:
                    Debug.Assert(firstByte == 'n');
                    resource = ExceptionResource.ExpectedNull;
                    break;
            }
            ThrowHelper.ThrowJsonReaderException(ref this, resource, bytes: span);
        }

        private bool ConsumeNumber()
        {
            if (!TryGetNumber(_buffer.Slice(_consumed), out int consumed))
            {
                return false;
            }

            _tokenType = JsonTokenType.Number;
            _consumed += consumed;
            _bytePositionInLine += consumed;

            if (_consumed >= (uint)_buffer.Length)
            {
                if (!_isNotPrimitive)
                {
                    return true;
                }
                if (IsLastSpan)
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, _buffer[_consumed - 1]);
                }
                return false;
            }

            // TODO: https://github.com/dotnet/corefx/issues/33294
            if (JsonConstants.Delimiters.IndexOf(_buffer[_consumed]) < 0)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, _buffer[_consumed]);
            }
            return true;
        }

        private bool ConsumePropertyName()
        {
            if (!ConsumeString())
            {
                return false;
            }

            if (!HasMoreData(ExceptionResource.ExpectedValueAfterPropertyNameNotFound))
            {
                return false;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            // We do not validate if 'first' is an invalid JSON byte here (such as control characters).
            // Those cases are captured below where we only accept ':'.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData(ExceptionResource.ExpectedValueAfterPropertyNameNotFound))
                {
                    return false;
                }
                first = _buffer[_consumed];
            }

            // The next character must be a key / value seperator. Validate and skip.
            if (first != JsonConstants.KeyValueSeperator)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedSeparatorAfterPropertyNameNotFound, first);
            }

            _consumed++;
            _bytePositionInLine++;
            _tokenType = JsonTokenType.PropertyName;
            return true;
        }

        private bool ConsumeString()
        {
            Debug.Assert(_buffer.Length >= _consumed + 1);
            Debug.Assert(_buffer[_consumed] == JsonConstants.Quote);

            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            // Vectorized search for either quote, backslash, or any control character.
            // If the first found byte is a quote, we have reached an end of string, and
            // can avoid validation.
            // Otherwise, in the uncommon case, iterate one character at a time and validate.
            int idx = localBuffer.IndexOfQuoteOrAnyControlOrBaskSlash();

            if (idx >= 0)
            {
                byte foundByte = localBuffer[idx];
                if (foundByte == JsonConstants.Quote)
                {
                    _bytePositionInLine += idx + 2; // Add 2 for the start and end quotes.
                    ValueSpan = localBuffer.Slice(0, idx);
                    _tokenType = JsonTokenType.String;
                    _consumed += idx + 2;
                    return true;
                }
                else
                {
                    return ConsumeStringAndValidate(localBuffer, idx);
                }
            }
            else
            {
                if (IsLastSpan)
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                }
                return false;
            }
        }

        // Found a backslash or control characters which are considered invalid within a string.
        // Search through the rest of the string one byte at a time.
        // https://tools.ietf.org/html/rfc8259#section-7
        private bool ConsumeStringAndValidate(ReadOnlySpan<byte> data, int idx)
        {
            Debug.Assert(idx >= 0 && idx < data.Length);
            Debug.Assert(data[idx] != JsonConstants.Quote);
            Debug.Assert(data[idx] == JsonConstants.BackSlash || data[idx] < JsonConstants.Space);

            long prevLineBytePosition = _bytePositionInLine;
            long prevLineNumber = _lineNumber;

            _bytePositionInLine += idx + 1; // Add 1 for the first quote

            bool nextCharEscaped = false;
            for (; idx < data.Length; idx++)
            {
                byte currentByte = data[idx];
                if (currentByte == JsonConstants.Quote)
                {
                    if (!nextCharEscaped)
                    {
                        goto Done;
                    }
                    nextCharEscaped = false;
                }
                else if (currentByte == JsonConstants.BackSlash)
                {
                    nextCharEscaped = !nextCharEscaped;
                }
                else if (nextCharEscaped)
                {
                    int index = JsonConstants.EscapableChars.IndexOf(currentByte);
                    if (index == -1)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, currentByte);
                    }

                    if (currentByte == JsonConstants.Quote)
                    {
                        // Ignore an escaped quote.
                        // This is likely the most common case, so adding an explicit check
                        // to avoid doing the unnecessary checks below.
                    }
                    else if (currentByte == 'n')
                    {
                        // Escaped new line character
                        _bytePositionInLine = -1; // Should be 0, but we increment _bytePositionInLine below already
                        _lineNumber++;
                    }
                    else if (currentByte == 'u')
                    {
                        // Expecting 4 hex digits to follow the escaped 'u'
                        _bytePositionInLine++;  // move past the 'u'
                        if (ValidateHexDigits(data, idx + 1))
                        {
                            idx += 4;   // Skip the 4 hex digits, the for loop accounts for idx incrementing past the 'u'
                        }
                        else
                        {
                            // We found less than 4 hex digits. Check if there is more data to follow, otherwise throw.
                            idx = data.Length;
                            break;
                        }

                    }
                    nextCharEscaped = false;
                }
                else if (currentByte < JsonConstants.Space)
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, currentByte);
                }

                _bytePositionInLine++;
            }

            if (idx >= data.Length)
            {
                if (IsLastSpan)
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                }
                _lineNumber = prevLineNumber;
                _bytePositionInLine = prevLineBytePosition;
                return false;
            }

        Done:
            _bytePositionInLine++;  // Add 1 for the end quote
            ValueSpan = data.Slice(0, idx);
            _tokenType = JsonTokenType.String;
            _consumed += idx + 2;
            return true;
        }

        private bool ValidateHexDigits(ReadOnlySpan<byte> data, int idx)
        {
            for (int j = idx; j < data.Length; j++)
            {
                byte nextByte = data[j];
                if (!JsonReaderHelper.IsHexDigit(nextByte))
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, nextByte);
                }
                if (j - idx >= 3)
                {
                    return true;
                }
                _bytePositionInLine++;
            }

            return false;
        }

        // https://tools.ietf.org/html/rfc7159#section-6
        private bool TryGetNumber(ReadOnlySpan<byte> data, out int consumed)
        {
            // TODO: https://github.com/dotnet/corefx/issues/33294
            Debug.Assert(data.Length > 0);

            consumed = 0;
            int i = 0;

            ConsumeNumberResult signResult = ConsumeNegativeSign(ref data, ref i);
            if (signResult == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }

            Debug.Assert(signResult == ConsumeNumberResult.OperationIncomplete);

            byte nextByte = data[i];
            Debug.Assert(nextByte >= '0' && nextByte <= '9');

            if (nextByte == '0')
            {
                ConsumeNumberResult result = ConsumeZero(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                Debug.Assert(result == ConsumeNumberResult.OperationIncomplete);
                nextByte = data[i];
            }
            else
            {
                i++;
                ConsumeNumberResult result = ConsumeIntegerDigits(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                Debug.Assert(result == ConsumeNumberResult.OperationIncomplete);
                nextByte = data[i];
                if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                {
                    _bytePositionInLine += i;
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                }
            }

            Debug.Assert(nextByte == '.' || nextByte == 'E' || nextByte == 'e');

            if (nextByte == '.')
            {
                i++;
                ConsumeNumberResult result = ConsumeDecimalDigits(ref data, ref i);
                if (result == ConsumeNumberResult.NeedMoreData)
                {
                    return false;
                }
                if (result == ConsumeNumberResult.Success)
                {
                    goto Done;
                }

                Debug.Assert(result == ConsumeNumberResult.OperationIncomplete);
                nextByte = data[i];
                if (nextByte != 'E' && nextByte != 'e')
                {
                    _bytePositionInLine += i;
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitEValueNotFound, nextByte);
                }
            }

            Debug.Assert(nextByte == 'E' || nextByte == 'e');
            i++;

            signResult = ConsumeSign(ref data, ref i);
            if (signResult == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }

            Debug.Assert(signResult == ConsumeNumberResult.OperationIncomplete);

            i++;
            ConsumeNumberResult resultExponent = ConsumeIntegerDigits(ref data, ref i);
            if (resultExponent == ConsumeNumberResult.NeedMoreData)
            {
                return false;
            }
            if (resultExponent == ConsumeNumberResult.Success)
            {
                goto Done;
            }

            Debug.Assert(resultExponent == ConsumeNumberResult.OperationIncomplete);

            _bytePositionInLine += i;
            ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);

        Done:
            ValueSpan = data.Slice(0, i);
            consumed = i;
            return true;
        }

        private ConsumeNumberResult ConsumeNegativeSign(ref ReadOnlySpan<byte> data, ref int i)
        {
            byte nextByte = data[i];

            if (nextByte == '-')
            {
                i++;
                if (i >= data.Length)
                {
                    if (IsLastSpan)
                    {
                        _bytePositionInLine += i;
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                    }
                    return ConsumeNumberResult.NeedMoreData;
                }

                nextByte = data[i];
                if (!JsonReaderHelper.IsDigit(nextByte))
                {
                    _bytePositionInLine += i;
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterSign, nextByte);
                }
            }
            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeZero(ref ReadOnlySpan<byte> data, ref int i)
        {
            Debug.Assert(data[i] == (byte)'0');
            i++;
            byte nextByte = default;
            if (i < data.Length)
            {
                nextByte = data[i];
                if (JsonConstants.Delimiters.IndexOf(nextByte) >= 0)
                {
                    return ConsumeNumberResult.Success;
                }
            }
            else
            {
                if (IsLastSpan)
                {
                    // A payload containing a single value: "0" is valid
                    // If we are v with multi-value JSON,
                    // ConsumeNumber will validate that we have a delimiter following the "0".
                    return ConsumeNumberResult.Success;
                }
                else
                {
                    return ConsumeNumberResult.NeedMoreData;
                }
            }
            nextByte = data[i];
            if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
            {
                _bytePositionInLine += i;
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeIntegerDigits(ref ReadOnlySpan<byte> data, ref int i)
        {
            byte nextByte = default;
            for (; i < data.Length; i++)
            {
                nextByte = data[i];
                if (!JsonReaderHelper.IsDigit(nextByte))
                {
                    break;
                }
            }
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    // A payload containing a single value of integers (e.g. "12") is valid
                    // If we are dealing with multi-value JSON,
                    // ConsumeNumber will validate that we have a delimiter following the integer.
                    return ConsumeNumberResult.Success;
                }
                else
                {
                    return ConsumeNumberResult.NeedMoreData;
                }
            }
            if (JsonConstants.Delimiters.IndexOf(nextByte) >= 0)
            {
                return ConsumeNumberResult.Success;
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private ConsumeNumberResult ConsumeDecimalDigits(ref ReadOnlySpan<byte> data, ref int i)
        {
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    _bytePositionInLine += i;
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                }
                return ConsumeNumberResult.NeedMoreData;
            }
            byte nextByte = data[i];
            if (!JsonReaderHelper.IsDigit(nextByte))
            {
                _bytePositionInLine += i;
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterDecimal, nextByte);
            }
            i++;

            return ConsumeIntegerDigits(ref data, ref i);
        }

        private ConsumeNumberResult ConsumeSign(ref ReadOnlySpan<byte> data, ref int i)
        {
            if (i >= data.Length)
            {
                if (IsLastSpan)
                {
                    _bytePositionInLine += i;
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                }
                return ConsumeNumberResult.NeedMoreData;
            }

            byte nextByte = data[i];
            if (nextByte == '+' || nextByte == '-')
            {
                i++;
                if (i >= data.Length)
                {
                    if (IsLastSpan)
                    {
                        _bytePositionInLine += i;
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundEndOfData);
                    }
                    return ConsumeNumberResult.NeedMoreData;
                }
                nextByte = data[i];
            }

            if (!JsonReaderHelper.IsDigit(nextByte))
            {
                _bytePositionInLine += i;
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.RequiredDigitNotFoundAfterSign, nextByte);
            }

            return ConsumeNumberResult.OperationIncomplete;
        }

        private bool ConsumeNextTokenOrRollback(byte marker)
        {
            int prevConsumed = _consumed;
            long prevPosition = _bytePositionInLine;
            long prevLineNumber = _lineNumber;
            JsonTokenType prevTokenType = _tokenType;
            ConsumeTokenResult result = ConsumeNextToken(marker);
            if (result == ConsumeTokenResult.Success)
            {
                return true;
            }
            if (result == ConsumeTokenResult.NotEnoughDataRollBackState)
            {
                _consumed = prevConsumed;
                _tokenType = prevTokenType;
                _bytePositionInLine = prevPosition;
                _lineNumber = prevLineNumber;
            }
            return false;
        }

        /// <summary>
        /// This method consumes the next token regardless of whether we are inside an object or an array.
        /// For an object, it reads the next property name token. For an array, it just reads the next value.
        /// </summary>
        private ConsumeTokenResult ConsumeNextToken(byte marker)
        {
            if (_readerOptions.CommentHandling != JsonCommentHandling.Disallow)
            {
                if (_readerOptions.CommentHandling == JsonCommentHandling.Allow)
                {
                    if (marker == JsonConstants.Slash)
                    {
                        return ConsumeComment() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    if (_tokenType == JsonTokenType.Comment)
                    {
                        return ConsumeNextTokenFromLastNonCommentToken();
                    }
                }
                else
                {
                    Debug.Assert(_readerOptions.CommentHandling == JsonCommentHandling.Skip);
                    return ConsumeNextTokenUntilAfterAllCommentsAreSkipped(marker);
                }
            }

            if (!_isNotPrimitive)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, marker);
            }

            if (marker == JsonConstants.ListSeperator)
            {
                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    return ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                byte first = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        return ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    first = _buffer[_consumed];
                }

                if (_readerOptions.CommentHandling == JsonCommentHandling.Allow && first == JsonConstants.Slash)
                {
                    return ConsumeComment() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }

                if (_inObject)
                {
                    if (first != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }
                    return ConsumePropertyName() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                else
                {
                    return ConsumeValue(first) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
            }
            else if (marker == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (marker == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.FoundInvalidCharacter, marker);
            }
            return ConsumeTokenResult.Success;
        }

        private ConsumeTokenResult ConsumeNextTokenFromLastNonCommentToken()
        {
            if (JsonReaderHelper.IsTokenTypePrimitive(_previousTokenType))
            {
                _tokenType = _inObject ? JsonTokenType.StartObject : JsonTokenType.StartArray;
            }
            else
            {
                _tokenType = _previousTokenType;
            }

            Debug.Assert(_tokenType != JsonTokenType.Comment);

            if (!HasMoreData())
            {
                goto RollBack;
            }

            byte first = _buffer[_consumed];

            // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpace();
                if (!HasMoreData())
                {
                    goto RollBack;
                }
                first = _buffer[_consumed];
            }

            if (!_isNotPrimitive && _tokenType != JsonTokenType.None)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, first);
            }

            Debug.Assert(first != JsonConstants.Slash);

            if (first == JsonConstants.ListSeperator)
            {
                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    goto RollBack;
                }
                first = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        goto RollBack;
                    }
                    first = _buffer[_consumed];
                }

                if (_inObject)
                {
                    if (first != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }
                    if (ConsumePropertyName())
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
                else
                {
                    if (ConsumeValue(first))
                    {
                        goto Done;
                    }
                    else
                    {
                        goto RollBack;
                    }
                }
            }
            else if (first == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (first == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else if (_tokenType == JsonTokenType.None)
            {
                if (ReadFirstToken(first))
                {
                    goto Done;
                }
                else
                {
                    goto RollBack;
                }
            }
            else if (_tokenType == JsonTokenType.StartObject)
            {
                if (first == JsonConstants.CloseBrace)
                {
                    EndObject();
                }
                else
                {
                    if (first != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                    }

                    int prevConsumed = _consumed;
                    long prevPosition = _bytePositionInLine;
                    long prevLineNumber = _lineNumber;
                    if (!ConsumePropertyName())
                    {
                        // roll back potential changes
                        _consumed = prevConsumed;
                        _tokenType = JsonTokenType.StartObject;
                        _bytePositionInLine = prevPosition;
                        _lineNumber = prevLineNumber;
                        goto RollBack;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {
                if (first == JsonConstants.CloseBracket)
                {
                    EndArray();
                }
                else
                {
                    if (!ConsumeValue(first))
                    {
                        goto RollBack;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                if (!ConsumeValue(first))
                {
                    goto RollBack;
                }
                goto Done;
            }
            else
            {
                goto RollBack;
            }

        Done:
            return ConsumeTokenResult.Success;

        RollBack:
            return ConsumeTokenResult.NotEnoughDataRollBackState;
        }

        private bool SkipAllComments(ref byte marker)
        {
            while (marker == JsonConstants.Slash)
            {
                if (SkipComment())
                {
                    if (!HasMoreData())
                    {
                        goto IncompleteNoRollback;
                    }

                    marker = _buffer[_consumed];

                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                    if (marker <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        if (!HasMoreData())
                        {
                            goto IncompleteNoRollback;
                        }
                        marker = _buffer[_consumed];
                    }
                }
                else
                {
                    goto IncompleteNoRollback;
                }
            }
            return true;

        IncompleteNoRollback:
            return false;
        }

        private bool SkipAllComments(ref byte marker, ExceptionResource resource)
        {
            while (marker == JsonConstants.Slash)
            {
                if (SkipComment())
                {
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(resource))
                    {
                        goto IncompleteRollback;
                    }

                    marker = _buffer[_consumed];

                    // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                    if (marker <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        // The next character must be a start of a property name or value.
                        if (!HasMoreData(resource))
                        {
                            goto IncompleteRollback;
                        }
                        marker = _buffer[_consumed];
                    }
                }
                else
                {
                    goto IncompleteRollback;
                }
            }
            return true;

        IncompleteRollback:
            return false;
        }

        private ConsumeTokenResult ConsumeNextTokenUntilAfterAllCommentsAreSkipped(byte marker)
        {
            if (!SkipAllComments(ref marker))
            {
                goto IncompleteNoRollback;
            }

            if (_tokenType == JsonTokenType.StartObject)
            {
                if (marker == JsonConstants.CloseBrace)
                {
                    EndObject();
                }
                else
                {
                    if (marker != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, marker);
                    }

                    int prevConsumed = _consumed;
                    long prevPosition = _bytePositionInLine;
                    long prevLineNumber = _lineNumber;
                    if (!ConsumePropertyName())
                    {
                        // roll back potential changes
                        _consumed = prevConsumed;
                        _tokenType = JsonTokenType.StartObject;
                        _bytePositionInLine = prevPosition;
                        _lineNumber = prevLineNumber;
                        goto IncompleteNoRollback;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.StartArray)
            {
                if (marker == JsonConstants.CloseBracket)
                {
                    EndArray();
                }
                else
                {
                    if (!ConsumeValue(marker))
                    {
                        goto IncompleteNoRollback;
                    }
                    goto Done;
                }
            }
            else if (_tokenType == JsonTokenType.PropertyName)
            {
                if (!ConsumeValue(marker))
                {
                    goto IncompleteNoRollback;
                }
                goto Done;
            }
            else if (!_isNotPrimitive)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, marker);
            }
            else if (marker == JsonConstants.ListSeperator)
            {
                _consumed++;
                _bytePositionInLine++;

                if (_consumed >= (uint)_buffer.Length)
                {
                    if (IsLastSpan)
                    {
                        _consumed--;
                        _bytePositionInLine--;
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                    }
                    return ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                marker = _buffer[_consumed];

                // This check is done as an optimization to avoid calling SkipWhiteSpace when not necessary.
                if (marker <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    // The next character must be a start of a property name or value.
                    if (!HasMoreData(ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                    {
                        return ConsumeTokenResult.NotEnoughDataRollBackState;
                    }
                    marker = _buffer[_consumed];
                }

                if (!SkipAllComments(ref marker, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound))
                {
                    goto IncompleteRollback;
                }

                if (_inObject)
                {
                    if (marker != JsonConstants.Quote)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, marker);
                    }
                    return ConsumePropertyName() ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
                else
                {
                    return ConsumeValue(marker) ? ConsumeTokenResult.Success : ConsumeTokenResult.NotEnoughDataRollBackState;
                }
            }
            else if (marker == JsonConstants.CloseBrace)
            {
                EndObject();
            }
            else if (marker == JsonConstants.CloseBracket)
            {
                EndArray();
            }
            else
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.FoundInvalidCharacter, marker);
            }

        Done:
            return ConsumeTokenResult.Success;
        IncompleteNoRollback:
            return ConsumeTokenResult.IncompleteNoRollBackNecessary;
        IncompleteRollback:
            return ConsumeTokenResult.NotEnoughDataRollBackState;
        }

        private bool SkipComment()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            if (localBuffer.Length > 0)
            {
                byte marker = localBuffer[0];
                if (marker == JsonConstants.Slash)
                {
                    return SkipSingleLineComment(localBuffer.Slice(1), out _);
                }
                else if (marker == JsonConstants.Asterisk)
                {
                    return SkipMultiLineComment(localBuffer.Slice(1), out _);
                }
                else
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }
            }

            if (IsLastSpan)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, JsonConstants.Slash);
            }
            return false;
        }

        private bool SkipSingleLineComment(ReadOnlySpan<byte> localBuffer, out int idx)
        {
            // TODO: https://github.com/dotnet/corefx/issues/33293
            idx = localBuffer.IndexOf(JsonConstants.LineFeed);
            if (idx == -1)
            {
                if (IsLastSpan)
                {
                    idx = localBuffer.Length;
                    // Assume everything on this line is a comment and there is no more data.
                    _bytePositionInLine += 2 + localBuffer.Length;
                    goto Done;
                }
                return false;
            }

            idx++;
            _bytePositionInLine = 0;
            _lineNumber++;
        Done:
            _consumed += 2 + idx;
            return true;
        }

        private bool SkipMultiLineComment(ReadOnlySpan<byte> localBuffer, out int idx)
        {
            idx = 0;
            while (true)
            {
                int foundIdx = localBuffer.Slice(idx).IndexOf(JsonConstants.Slash);
                if (foundIdx == -1)
                {
                    if (IsLastSpan)
                    {
                        ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.EndOfCommentNotFound);
                    }
                    return false;
                }
                if (foundIdx != 0 && localBuffer[foundIdx + idx - 1] == JsonConstants.Asterisk)
                {
                    idx += foundIdx;
                    break;
                }
                idx += foundIdx + 1;
            }

            Debug.Assert(idx >= 1);

            // Consume the /* and */ characters that are part of the multi-line comment.
            // Since idx is pointing at right after the final '*' (i.e. before the last '/'), we don't need to count that character.
            // Hence, we increment consumed by 3 (instead of 4).
            _consumed += 4 + idx - 1;

            (int newLines, int newLineIndex) = JsonReaderHelper.CountNewLines(localBuffer.Slice(0, idx - 1));
            _lineNumber += newLines;
            if (newLineIndex != -1)
            {
                _bytePositionInLine = idx - newLineIndex;
            }
            else
            {
                _bytePositionInLine += 4 + idx - 1;
            }
            return true;
        }

        private bool ConsumeComment()
        {
            // Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localBuffer = _buffer.Slice(_consumed + 1);

            if (localBuffer.Length > 0)
            {
                byte marker = localBuffer[0];
                if (marker == JsonConstants.Slash)
                {
                    return ConsumeSingleLineComment(localBuffer.Slice(1), _consumed);
                }
                else if (marker == JsonConstants.Asterisk)
                {
                    return ConsumeMultiLineComment(localBuffer.Slice(1), _consumed);
                }
                else
                {
                    ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }
            }

            if (IsLastSpan)
            {
                ThrowHelper.ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, JsonConstants.Slash);
            }
            return false;
        }

        private bool ConsumeSingleLineComment(ReadOnlySpan<byte> localBuffer, int previousConsumed)
        {
            if (!SkipSingleLineComment(localBuffer, out int idx))
            {
                return false;
            }

            ValueSpan = _buffer.Slice(previousConsumed, idx + 2);   // Include the double slash and potential line feed at the end of the comment as part of it.
            if (_tokenType != JsonTokenType.Comment)
            {
                _previousTokenType = _tokenType;
            }
            _tokenType = JsonTokenType.Comment;
            return true;
        }

        private bool ConsumeMultiLineComment(ReadOnlySpan<byte> localBuffer, int previousConsumed)
        {
            if (!SkipMultiLineComment(localBuffer, out int idx))
            {
                return false;
            }

            ValueSpan = _buffer.Slice(previousConsumed, idx + 3); // Include the slash/asterisk and final slash at the end of the comment as part of it.
            if (_tokenType != JsonTokenType.Comment)
            {
                _previousTokenType = _tokenType;
            }
            _tokenType = JsonTokenType.Comment;
            return true;
        }
    }
}
