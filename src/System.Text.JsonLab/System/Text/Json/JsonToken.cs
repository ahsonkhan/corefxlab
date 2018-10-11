// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers.Reader;
using System.Diagnostics;
using static System.Text.JsonLab.JsonThrowHelper;

namespace System.Text.JsonLab
{
    public partial class JsonReader
    {
        public ref partial struct JsonToken
        {
            internal JsonReader _state;

            private readonly ReadOnlySpan<byte> _buffer;

            internal long TokenStartIndex { get; private set; }

            private BufferReader<byte> _reader;

            /// <summary>
            /// Gets the value as a ReadOnlySpan<byte> of the last processed token. The contents of this
            /// is only relevant when <see cref="TokenType" /> is <see cref="JsonTokenType.Value" /> or
            /// <see cref="JsonTokenType.PropertyName" />. Otherwise, this value should be set to
            /// <see cref="ReadOnlySpan{T}.Empty"/>.
            /// </summary>
            public ReadOnlySpan<byte> Value { get; private set; }

            /// <summary>
            /// Gets the JSON value type of the last processed token. The contents of this
            /// is only relevant when <see cref="TokenType" /> is <see cref="JsonTokenType.Value" /> or
            /// <see cref="JsonTokenType.PropertyName" />.
            /// </summary>
            public JsonValueType ValueType { get; private set; }

            // Depth tracks the recursive depth of the nested objects / arrays within the JSON data.
            public int Depth => _state._depth;

            public long Consumed { get; private set; }

            /// <summary>
            /// Gets the token type of the last processed token in the JSON stream.
            /// </summary>
            public JsonTokenType TokenType => _state._tokenType;

            private readonly bool _isSingleSegment;
            private readonly bool _isFinalBlock;
            private bool _isSingleValue;

            internal bool ConsumedEverything => _isSingleSegment ? Consumed >= (uint)_buffer.Length : _reader.End;

            internal JsonToken(JsonReader jsonReader, ReadOnlySpan<byte> data, bool isFinalBlock)
            {
                if (jsonReader.IsDefault)
                {
                    _state = jsonReader;
                    _state._lineNumber = 1;
                    _state._maxDepth = StackFreeMaxDepth;
                }
                else
                {
                    _state = jsonReader;
                    _state._containerMask = jsonReader._containerMask;
                    _state._depth = jsonReader._depth;
                    _state._inObject = jsonReader._inObject;
                    _state._stack = jsonReader._stack;
                    _state._tokenType = jsonReader._tokenType;
                    _state._lineNumber = jsonReader._lineNumber;
                    _state._position = jsonReader._position;
                    _state._maxDepth = jsonReader._maxDepth;
                    _state._allowComments = jsonReader._allowComments;
                }

                _isFinalBlock = isFinalBlock;

                _reader = default;
                _isSingleSegment = true;
                _buffer = data;
                Consumed = 0;
                TokenStartIndex = Consumed;
                Value = ReadOnlySpan<byte>.Empty;
                ValueType = JsonValueType.Unknown;
                _isSingleValue = true;
            }

            /// <summary>
            /// Read the next token from the data buffer.
            /// </summary>
            /// <returns>True if the token was read successfully, else false.</returns>
            public bool MoveNext()
            {
                return _isSingleSegment ? ReadSingleSegment() : ReadMultiSegment(ref _reader);
            }

            public JsonToken Current => this;

            public JsonToken GetEnumerator() => this;

            public void Skip()
            {
                if (_state._tokenType == JsonTokenType.PropertyName)
                {
                    MoveNext();
                }

                if (_state._tokenType == JsonTokenType.StartObject || _state._tokenType == JsonTokenType.StartArray)
                {
                    int depth = _state._depth;
                    while (MoveNext() && depth < _state._depth)
                    {
                    }
                }
            }

            private void StartObject()
            {
                _state._depth++;
                if (_state._depth > _state.MaxDepth)
                    ThrowJsonReaderException(ref this, ExceptionResource.ObjectDepthTooLarge);

                _state._position++;

                if (_state._depth <= StackFreeMaxDepth)
                    _state._containerMask = (_state._containerMask << 1) | 1;
                else
                    _state._stack.Push(InternalJsonTokenType.StartObject);

                _state._tokenType = JsonTokenType.StartObject;
                _state._inObject = true;
            }

            private void EndObject()
            {
                if (!_state._inObject || _state._depth <= 0)
                    ThrowJsonReaderException(ref this, ExceptionResource.ObjectEndWithinArray);

                if (_state._depth <= StackFreeMaxDepth)
                {
                    _state._containerMask >>= 1;
                    _state._inObject = (_state._containerMask & 1) != 0;
                }
                else
                {
                    _state._inObject = _state._stack.Pop() != InternalJsonTokenType.StartArray;
                }

                _state._depth--;
                _state._tokenType = JsonTokenType.EndObject;
            }

            private void StartArray()
            {
                _state._depth++;
                if (_state._depth > _state.MaxDepth)
                    ThrowJsonReaderException(ref this, ExceptionResource.ArrayDepthTooLarge);

                _state._position++;

                if (_state._depth <= StackFreeMaxDepth)
                    _state._containerMask = _state._containerMask << 1;
                else
                    _state._stack.Push(InternalJsonTokenType.StartArray);

                _state._tokenType = JsonTokenType.StartArray;
                _state._inObject = false;
            }

            private void EndArray()
            {
                if (_state._inObject || _state._depth <= 0)
                    ThrowJsonReaderException(ref this, ExceptionResource.ArrayEndWithinObject);

                if (_state._depth <= StackFreeMaxDepth)
                {
                    _state._containerMask >>= 1;
                    _state._inObject = (_state._containerMask & 1) != 0;
                }
                else
                {
                    _state._inObject = _state._stack.Pop() != InternalJsonTokenType.StartArray;
                }

                _state._depth--;
                _state._tokenType = JsonTokenType.EndArray;
            }

            private bool ReadFirstToken(byte first)
            {
                if (first == JsonConstants.OpenBrace)
                {
                    _state._depth++;
                    _state._containerMask = 1;
                    _state._tokenType = JsonTokenType.StartObject;
                    ValueType = JsonValueType.Object;
                    Consumed++;
                    _state._position++;
                    _state._inObject = true;
                    _isSingleValue = false;
                }
                else if (first == JsonConstants.OpenBracket)
                {
                    _state._depth++;
                    _state._tokenType = JsonTokenType.StartArray;
                    ValueType = JsonValueType.Array;
                    Consumed++;
                    _state._position++;
                    _isSingleValue = false;
                }
                else
                {
                    if ((uint)(first - '0') <= '9' - '0' || first == '-')
                    {
                        ValueType = JsonValueType.Number;
                        if (!TryGetNumber(_buffer.Slice((int)Consumed), out ReadOnlySpan<byte> number))
                            return false;
                        Value = number;
                        _state._tokenType = JsonTokenType.Value;
                        Consumed += Value.Length;
                        _state._position += Value.Length;
                        goto Done;
                    }
                    else if (ConsumeValue(first))
                    {
                        goto Done;
                    }

                    return false;

                Done:
                    if (Consumed >= (uint)_buffer.Length)
                    {
                        return true;
                    }

                    if (_buffer[(int)Consumed] <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        if (Consumed >= (uint)_buffer.Length)
                        {
                            return true;
                        }
                    }

                    if (_state.AllowComments)
                    {
                        if (_state._tokenType == JsonTokenType.Comment || _buffer[(int)Consumed] == JsonConstants.Solidus)
                        {
                            return true;
                        }
                    }
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, _buffer[(int)Consumed]);
                }
                return true;
            }

            private bool ReadSingleSegment()
            {
                bool retVal = false;

                if (Consumed >= (uint)_buffer.Length)
                {
                    if (!_isSingleValue && _isFinalBlock)
                    {
                        if (_state._tokenType != JsonTokenType.EndArray && _state._tokenType != JsonTokenType.EndObject)
                            ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                    }
                    goto Done;
                }

                byte first = _buffer[(int)Consumed];

                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    if (Consumed >= (uint)_buffer.Length)
                    {
                        if (!_isSingleValue && _isFinalBlock)
                        {
                            if (_state._tokenType != JsonTokenType.EndArray && _state._tokenType != JsonTokenType.EndObject)
                                ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                        }
                        goto Done;
                    }
                    first = _buffer[(int)Consumed];
                }

                TokenStartIndex = Consumed;

                if (_state._tokenType == JsonTokenType.None)
                {
                    goto ReadFirstToken;
                }

                if (_state._tokenType == JsonTokenType.StartObject)
                {
                    if (first == JsonConstants.CloseBrace)
                    {
                        Consumed++;
                        _state._position++;
                        EndObject();
                    }
                    else
                    {
                        if (first != JsonConstants.Quote)
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);

                        TokenStartIndex++;
                        long prevConsumed = Consumed;
                        long prevPosition = _state._position;
                        if (ConsumePropertyName())
                        {
                            return true;
                        }
                        Consumed = prevConsumed;
                        _state._tokenType = JsonTokenType.StartObject;
                        _state._position = prevPosition;
                        return false;
                    }
                }
                else if (_state._tokenType == JsonTokenType.StartArray)
                {
                    if (first == JsonConstants.CloseBracket)
                    {
                        Consumed++;
                        _state._position++;
                        EndArray();
                    }
                    else
                    {
                        return ConsumeValue(first);
                    }
                }
                else if (_state._tokenType == JsonTokenType.PropertyName)
                {
                    return ConsumeValue(first);
                }
                else
                {
                    long prevConsumed = Consumed;
                    long prevPosition = _state._position;
                    JsonTokenType prevTokenType = _state._tokenType;
                    if (ConsumeNextToken(first))
                    {
                        return true;
                    }
                    Consumed = prevConsumed;
                    _state._tokenType = prevTokenType;
                    _state._position = prevPosition;
                    return false;
                }

                retVal = true;

            Done:
                return retVal;

            ReadFirstToken:
                retVal = ReadFirstToken(first);
                goto Done;
            }

            /// <summary>
            /// This method consumes the next token regardless of whether we are inside an object or an array.
            /// For an object, it reads the next property name token. For an array, it just reads the next value.
            /// </summary>
            private bool ConsumeNextToken(byte marker)
            {
                Consumed++;
                _state._position++;

                if (_state.AllowComments)
                {
                    if (marker == JsonConstants.Solidus)
                    {
                        Consumed--;
                        _state._position--;
                        return ConsumeComment();
                    }
                    if (_state._tokenType == JsonTokenType.Comment)
                    {
                        Consumed--;
                        _state._position--;
                        _state._tokenType = (JsonTokenType)_state._stack.Pop();
                        return ReadSingleSegment();
                    }
                }

                if (marker == JsonConstants.ListSeperator)
                {
                    if (Consumed >= (uint)_buffer.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                        }
                        else return false;
                    }
                    byte first = _buffer[(int)Consumed];

                    if (first <= JsonConstants.Space)
                    {
                        SkipWhiteSpace();
                        // The next character must be a start of a property name or value.
                        if (Consumed >= (uint)_buffer.Length)
                        {
                            if (_isFinalBlock)
                            {
                                ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyOrValueNotFound);
                            }
                            else return false;
                        }
                        first = _buffer[(int)Consumed];
                    }

                    TokenStartIndex = Consumed;
                    if (_state._inObject)
                    {
                        if (first != JsonConstants.Quote)
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfPropertyNotFound, first);
                        TokenStartIndex++;
                        return ConsumePropertyName();
                    }
                    else
                    {
                        return ConsumeValue(first);
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
                    Consumed--;
                    _state._position--;
                    ThrowJsonReaderException(ref this, ExceptionResource.FoundInvalidCharacter, marker);
                }
                return true;
            }

            /// <summary>
            /// This method contains the logic for processing the next value token and determining
            /// what type of data it is.
            /// </summary>
            private bool ConsumeValue(byte marker)
            {
                if (marker == JsonConstants.Quote)
                {
                    TokenStartIndex++;
                    return ConsumeString();
                }
                else if (marker == JsonConstants.OpenBrace)
                {
                    Consumed++;
                    StartObject();
                    ValueType = JsonValueType.Object;
                }
                else if (marker == JsonConstants.OpenBracket)
                {
                    Consumed++;
                    StartArray();
                    ValueType = JsonValueType.Array;
                }
                else if ((uint)(marker - '0') <= '9' - '0' || marker == '-')
                {
                    return ConsumeNumber();
                }
                else if (marker == 'f')
                {
                    return ConsumeFalse();
                }
                else if (marker == 't')
                {
                    return ConsumeTrue();
                }
                else if (marker == 'n')
                {
                    return ConsumeNull();
                }
                else
                {
                    if (_state.AllowComments && marker == JsonConstants.Solidus)
                        return ConsumeComment();

                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }
                return true;
            }

            private bool ConsumeComment()
            {
                //Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localCopy = _buffer.Slice((int)Consumed + 1);

                if (localCopy.Length > 0)
                {
                    byte marker = localCopy[0];
                    if (marker == JsonConstants.Solidus)
                        return ConsumeSingleLineComment(localCopy.Slice(1));
                    else if (marker == '*')
                        return ConsumeMultiLineComment(localCopy.Slice(1));
                    else
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, marker);
                }

                if (_isFinalBlock)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedStartOfValueNotFound, JsonConstants.Solidus);
                }
                else return false;

                return true;
            }

            private bool ConsumeSingleLineComment(ReadOnlySpan<byte> localCopy)
            {
                int idx = localCopy.IndexOf(JsonConstants.LineFeed);
                if (idx == -1)
                {
                    if (_isFinalBlock)
                    {
                        // Assume everything on this line is a comment and there is no more data.
                        Value = localCopy;
                        _state._position += 2 + Value.Length;
                        goto Done;
                    }
                    else return false;
                }

                Value = localCopy.Slice(0, idx);
                Consumed++;
                _state._position = 0;
                _state._lineNumber++;
            Done:
                _state._stack.Push((InternalJsonTokenType)_state._tokenType);
                _state._tokenType = JsonTokenType.Comment;
                Consumed += 2 + Value.Length;
                return true;
            }

            private bool ConsumeMultiLineComment(ReadOnlySpan<byte> localCopy)
            {
                int idx = 0;
                while (true)
                {
                    int foundIdx = localCopy.Slice(idx).IndexOf(JsonConstants.Solidus);
                    if (foundIdx == -1)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.EndOfCommentNotFound);
                        }
                        else return false;
                    }
                    if (foundIdx != 0 && localCopy[foundIdx + idx - 1] == '*')
                    {
                        idx += foundIdx;
                        break;
                    }
                    idx += foundIdx + 1;
                }

                Debug.Assert(idx >= 1);
                _state._stack.Push((InternalJsonTokenType)_state._tokenType);
                Value = localCopy.Slice(0, idx - 1);
                _state._tokenType = JsonTokenType.Comment;
                Consumed += 4 + Value.Length;

                (int newLines, int newLineIndex) = JsonReaderHelper.CountNewLines(Value);
                _state._lineNumber += newLines;
                if (newLineIndex != -1)
                {
                    _state._position = Value.Length - newLineIndex + 1;
                }
                else
                {
                    _state._position += 4 + Value.Length;
                }
                return true;
            }

            private bool ConsumeNumber()
            {
                ValueType = JsonValueType.Number;
                if (!TryGetNumberLookForEnd(_buffer.Slice((int)Consumed), out ReadOnlySpan<byte> number))
                    return false;
                Value = number;
                _state._tokenType = JsonTokenType.Value;
                Consumed += Value.Length;
                _state._position += Value.Length;
                return true;
            }

            private bool ConsumeNull()
            {
                Value = JsonConstants.NullValue;
                ValueType = JsonValueType.Null;

                ReadOnlySpan<byte> span = _buffer.Slice((int)Consumed);

                Debug.Assert(span.Length > 0 && span[0] == Value[0]);

                if (!span.StartsWith(Value))
                {
                    if (_isFinalBlock)
                    {
                        goto Throw;
                    }
                    else
                    {
                        if (span.Length > 1 && span[1] != Value[1])
                            goto Throw;
                        if (span.Length > 2 && span[2] != Value[2])
                            goto Throw;
                        if (span.Length >= Value.Length)
                            goto Throw;
                        return false;
                    }
                Throw:
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNull, bytes: span);
                }
                _state._tokenType = JsonTokenType.Value;
                Consumed += 4;
                _state._position += 4;
                return true;
            }

            private bool ConsumeFalse()
            {
                Value = JsonConstants.FalseValue;
                ValueType = JsonValueType.False;

                ReadOnlySpan<byte> span = _buffer.Slice((int)Consumed);

                Debug.Assert(span.Length > 0 && span[0] == Value[0]);

                if (!span.StartsWith(Value))
                {
                    if (_isFinalBlock)
                    {
                        goto Throw;
                    }
                    else
                    {
                        if (span.Length > 1 && span[1] != Value[1])
                            goto Throw;
                        if (span.Length > 2 && span[2] != Value[2])
                            goto Throw;
                        if (span.Length > 3 && span[3] != Value[3])
                            goto Throw;
                        if (span.Length >= Value.Length)
                            goto Throw;
                        return false;
                    }
                Throw:
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedFalse, bytes: span);
                }
                _state._tokenType = JsonTokenType.Value;
                Consumed += 5;
                _state._position += 5;
                return true;
            }

            private bool ConsumeTrue()
            {
                Value = JsonConstants.TrueValue;
                ValueType = JsonValueType.True;

                ReadOnlySpan<byte> span = _buffer.Slice((int)Consumed);

                Debug.Assert(span.Length > 0 && span[0] == Value[0]);

                if (!span.StartsWith(Value))
                {
                    if (_isFinalBlock)
                    {
                        goto Throw;
                    }
                    else
                    {
                        if (span.Length > 1 && span[1] != Value[1])
                            goto Throw;
                        if (span.Length > 2 && span[2] != Value[2])
                            goto Throw;
                        if (span.Length >= Value.Length)
                            goto Throw;
                        return false;
                    }
                Throw:
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedTrue, bytes: span);
                }
                _state._tokenType = JsonTokenType.Value;
                Consumed += 4;
                _state._position += 4;
                return true;
            }

            private bool ConsumePropertyName()
            {
                if (!ConsumeString())
                    return false;

                //Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localCopy = _buffer;
                if (Consumed >= (uint)localCopy.Length)
                {
                    if (_isFinalBlock)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedValueAfterPropertyNameNotFound);
                    }
                    else return false;
                }

                byte first = localCopy[(int)Consumed];

                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace();
                    if (Consumed >= (uint)localCopy.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedValueAfterPropertyNameNotFound);
                        }
                        else return false;
                    }
                    first = localCopy[(int)Consumed];
                }

                // The next character must be a key / value seperator. Validate and skip.
                if (first != JsonConstants.KeyValueSeperator)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedSeparaterAfterPropertyNameNotFound, first);
                }

                Consumed++;
                _state._position++;
                _state._tokenType = JsonTokenType.PropertyName;
                return true;
            }

            private bool ConsumeString()
            {
                Debug.Assert(_buffer.Length >= Consumed + 1);
                Debug.Assert(_buffer[(int)Consumed] == JsonConstants.Quote);

                return ConsumeStringVectorized();
            }

            private bool ConsumeStringVectorized()
            {
                //Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localCopy = _buffer;

                int idx = localCopy.Slice((int)Consumed + 1).IndexOf(JsonConstants.Quote);
                if (idx < 0)
                {
                    if (_isFinalBlock)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                    }
                    else return false;
                }

                if (localCopy[idx + (int)Consumed] != JsonConstants.ReverseSolidus)
                {
                    localCopy = localCopy.Slice((int)Consumed + 1, idx);

                    if (localCopy.IndexOfAnyControlOrEscape() != -1)
                    {
                        _state._position++;
                        if (ValidateEscaping_AndHex(localCopy))
                            goto Done;
                        return false;
                    }

                    _state._position += idx + 1;

                Done:
                    _state._position++;
                    Value = localCopy;
                    ValueType = JsonValueType.String;
                    _state._tokenType = JsonTokenType.Value;
                    Consumed += idx + 2;

                    return true;
                }
                else
                {
                    return ConsumeStringWithNestedQuotes();
                }
            }

            // https://tools.ietf.org/html/rfc8259#section-7
            private bool ValidateEscaping_AndHex(ReadOnlySpan<byte> data)
            {
                bool nextCharEscaped = false;
                for (int i = 0; i < data.Length; i++)
                {
                    byte currentByte = data[i];
                    if (currentByte == JsonConstants.ReverseSolidus)
                    {
                        nextCharEscaped = !nextCharEscaped;
                    }
                    else if (nextCharEscaped)
                    {
                        int index = JsonConstants.EscapableChars.IndexOf(currentByte);
                        if (index == -1)
                            ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, currentByte);

                        if (currentByte == 'n')
                        {
                            _state._position = -1; // Should be 0, but we increment _position below already
                            _state._lineNumber++;
                        }
                        else if (currentByte == 'u')
                        {
                            _state._position++;
                            int startIndex = i + 1;
                            for (int j = startIndex; j < data.Length; j++)
                            {
                                byte nextByte = data[j];
                                if ((uint)(nextByte - '0') > '9' - '0' && (uint)(nextByte - 'A') > 'F' - 'A' && (uint)(nextByte - 'a') > 'f' - 'a')
                                {
                                    ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, nextByte);
                                }
                                if (j - startIndex >= 4)
                                    break;
                                _state._position++;
                            }
                            i += 4;
                            if (i >= data.Length)
                            {
                                if (_isFinalBlock)
                                    ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                                else
                                    goto False;
                            }
                        }
                        nextCharEscaped = false;
                    }
                    else if (currentByte < JsonConstants.Space)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.InvalidCharacterWithinString, currentByte);
                    }
                    _state._position++;
                }

                return true;

            False:
                return false;
            }

            private bool ConsumeStringWithNestedQuotes()
            {
                //TODO: Optimize looking for nested quotes
                //TODO: Avoid redoing first IndexOf search
                long i = Consumed + 1;
                while (true)
                {
                    int counter = 0;
                    int foundIdx = _buffer.Slice((int)i).IndexOf(JsonConstants.Quote);
                    if (foundIdx == -1)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                        }
                        else return false;
                    }
                    if (foundIdx == 0)
                        break;
                    for (long j = i + foundIdx - 1; j >= i; j--)
                    {
                        if (_buffer[(int)j] != JsonConstants.ReverseSolidus)
                        {
                            if (counter % 2 == 0)
                            {
                                i += foundIdx;
                                goto FoundEndOfString;
                            }
                            break;
                        }
                        else
                            counter++;
                    }
                    i += foundIdx + 1;
                }

            FoundEndOfString:
                long startIndex = Consumed + 1;
                ReadOnlySpan<byte> localCopy = _buffer.Slice((int)startIndex, (int)(i - startIndex));

                if (localCopy.IndexOfAnyControlOrEscape() != -1)
                {
                    _state._position++;
                    if (ValidateEscaping_AndHex(localCopy))
                        goto Done;
                    return false;
                }

                _state._position = i;
            Done:
                _state._position++;
                Value = localCopy;
                ValueType = JsonValueType.String;
                _state._tokenType = JsonTokenType.Value;
                Consumed = i + 1;

                return true;
            }

            private void SkipWhiteSpace()
            {
                //Create local copy to avoid bounds checks.
                ReadOnlySpan<byte> localCopy = _buffer;
                for (; Consumed < localCopy.Length; Consumed++)
                {
                    byte val = localCopy[(int)Consumed];
                    if (val != JsonConstants.Space &&
                        val != JsonConstants.CarriageReturn &&
                        val != JsonConstants.LineFeed &&
                        val != JsonConstants.Tab)
                    {
                        break;
                    }

                    if (val == JsonConstants.LineFeed)
                    {
                        _state._lineNumber++;
                        _state._position = 0;
                    }
                    else
                    {
                        _state._position++;
                    }
                }
            }

            // https://tools.ietf.org/html/rfc7159#section-6
            private bool TryGetNumber(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> number)
            {
                Debug.Assert(data.Length > 0);

                ReadOnlySpan<byte> delimiters = JsonConstants.Delimiters;

                number = default;

                int i = 0;
                byte nextByte = data[i];

                if (nextByte == '-')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }

                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);
                }

                Debug.Assert(nextByte >= '0' && nextByte <= '9');

                if (nextByte == '0')
                {
                    i++;
                    if (i < data.Length)
                    {
                        nextByte = data[i];
                        if (delimiters.IndexOf(nextByte) != -1)
                            goto Done;

                        if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
                    }
                    else
                    {
                        if (_isFinalBlock)
                            goto Done;
                        else return false;
                    }
                }
                else
                {
                    i++;
                    for (; i < data.Length; i++)
                    {
                        nextByte = data[i];
                        if ((uint)(nextByte - '0') > '9' - '0')
                            break;
                    }
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                            goto Done;
                        else return false;
                    }
                    if (delimiters.IndexOf(nextByte) != -1)
                        goto Done;
                    if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
                }

                Debug.Assert(nextByte == '.' || nextByte == 'E' || nextByte == 'e');

                if (nextByte == '.')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }
                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);
                    i++;
                    for (; i < data.Length; i++)
                    {
                        nextByte = data[i];
                        if ((uint)(nextByte - '0') > '9' - '0')
                            break;
                    }
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                            goto Done;
                        else return false;
                    }
                    if (delimiters.IndexOf(nextByte) != -1)
                        goto Done;
                    if (nextByte != 'E' && nextByte != 'e')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitEValueNotFound, nextByte);
                }

                Debug.Assert(nextByte == 'E' || nextByte == 'e');
                i++;

                if (i >= data.Length)
                {
                    if (_isFinalBlock)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                    }
                    else return false;
                }

                nextByte = data[i];
                if (nextByte == '+' || nextByte == '-')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }
                    nextByte = data[i];
                }

                if ((uint)(nextByte - '0') > '9' - '0')
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);

                i++;
                for (; i < data.Length; i++)
                {
                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        break;
                }

                if (i < data.Length)
                {
                    if (delimiters.IndexOf(nextByte) == -1)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                    }
                }
                else if (!_isFinalBlock)
                {
                    return false;
                }

            Done:
                number = data.Slice(0, i);
                return true;
            }

            // TODO: Avoid code duplication if possible
            // https://tools.ietf.org/html/rfc7159#section-6
            private bool TryGetNumberLookForEnd(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> number)
            {
                Debug.Assert(data.Length > 0);

                ReadOnlySpan<byte> delimiters = JsonConstants.Delimiters;

                number = default;

                int i = 0;
                byte nextByte = data[i];

                if (nextByte == '-')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }

                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);
                }

                Debug.Assert(nextByte >= '0' && nextByte <= '9');

                if (nextByte == '0')
                {
                    i++;
                    if (i < data.Length)
                    {
                        nextByte = data[i];
                        if (delimiters.IndexOf(nextByte) != -1)
                            goto Done;

                        if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
                    }
                    else
                    {
                        if (_isFinalBlock)
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                        else return false;
                    }
                }
                else
                {
                    i++;
                    for (; i < data.Length; i++)
                    {
                        nextByte = data[i];
                        if ((uint)(nextByte - '0') > '9' - '0')
                            break;
                    }
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                        else return false;
                    }
                    if (delimiters.IndexOf(nextByte) != -1)
                        goto Done;
                    if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
                }

                Debug.Assert(nextByte == '.' || nextByte == 'E' || nextByte == 'e');

                if (nextByte == '.')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }
                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);
                    i++;
                    for (; i < data.Length; i++)
                    {
                        nextByte = data[i];
                        if ((uint)(nextByte - '0') > '9' - '0')
                            break;
                    }
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                        else return false;
                    }
                    if (delimiters.IndexOf(nextByte) != -1)
                        goto Done;
                    if (nextByte != 'E' && nextByte != 'e')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitEValueNotFound, nextByte);
                }

                Debug.Assert(nextByte == 'E' || nextByte == 'e');
                i++;

                if (i >= data.Length)
                {
                    if (_isFinalBlock)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                    }
                    else return false;
                }

                nextByte = data[i];
                if (nextByte == '+' || nextByte == '-')
                {
                    i++;
                    if (i >= data.Length)
                    {
                        if (_isFinalBlock)
                        {
                            ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                        }
                        else return false;
                    }
                    nextByte = data[i];
                }

                if ((uint)(nextByte - '0') > '9' - '0')
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);

                i++;
                for (; i < data.Length; i++)
                {
                    nextByte = data[i];
                    if ((uint)(nextByte - '0') > '9' - '0')
                        break;
                }

                if (i < data.Length)
                {
                    if (delimiters.IndexOf(nextByte) == -1)
                    {
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndOfDigitNotFound, nextByte);
                    }
                }
                else if (!_isFinalBlock)
                {
                    return false;
                }

            Done:
                number = data.Slice(0, i);
                return true;
            }
        }
    }
}
