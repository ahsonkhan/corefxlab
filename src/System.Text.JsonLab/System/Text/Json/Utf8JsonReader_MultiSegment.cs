﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

//TODO: Add multi-segment support for tracking line number/position

using System.Buffers;
using System.Diagnostics;
using static System.Text.JsonLab.JsonThrowHelper;

namespace System.Text.JsonLab
{
    public ref partial struct Utf8JsonReader
    {
        private bool ReadFirstTokenMultiSegment(byte first)
        {
            if (first == JsonConstants.OpenBrace)
            {
                Depth++;
                _containerMask = 1;
                TokenType = JsonTokenType.StartObject;
                _consumed++;
                _position++;
                _inObject = true;
                _isSingleValue = false;
            }
            else if (first == JsonConstants.OpenBracket)
            {
                Depth++;
                TokenType = JsonTokenType.StartArray;
                _consumed++;
                _position++;
                _isSingleValue = false;
            }
            else
            {
                // Cannot call ConsumeNumber since it looks for the delimiter and we won't find it.
                if ((uint)(first - '0') <= '9' - '0' || first == '-')
                {
                    if (!TryGetNumber(_buffer.Slice((int)_consumed), out ReadOnlySpan<byte> number))
                    {
                        if (!IsLastSpan)
                        {
                            if (!GetNextSpan())
                                ThrowJsonReaderException(ref this);
                            return ReadFirstTokenMultiSegment(first);
                        }
                        return false;
                    }




                    if (first == '-')
                    {
                        // Couldn't find anything beyond '-', can't advance past it since we need '-' to be part of the value
                        // TODO: This could silently allocate. Try to avoid it.
                        if (reader.Peek(2).Length == 1)
                        {
                            if (_isFinalBlock)
                            {
                                ThrowJsonReaderException(ref this);
                            }
                            else return false;
                        }
                    }
                    ReadOnlySequence<byte> sequence = reader.Sequence.Slice(reader.Position);

                    if (!TryGetNumber(sequence.IsSingleSegment ? sequence.First.Span : sequence.ToArray(), out ReadOnlySpan<byte> number))
                        return false;
                    if (_isFinalBlock)
                    {
                        Value = number;
                        TokenType = JsonTokenType.Number;
                        reader.Advance(Value.Length);
                        _consumed += Value.Length;
                        _position += Value.Length;
                        goto Done;
                    }
                    else return false;
                }
                else if (ConsumeValue(ref reader, first))
                {
                    goto Done;
                }

                return false;

            Done:
                if (!reader.TryPeek(out first))
                {
                    return true;
                }
                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpace(ref reader);
                    if (reader.End)
                    {
                        return true;
                    }
                }

                if (_readerOptions != JsonReaderOptions.Default)
                {
                    reader.TryPeek(out first);
                    if (_readerOptions == JsonReaderOptions.AllowComments)
                    {
                        if (TokenType == JsonTokenType.Comment || first == JsonConstants.Solidus)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // JsonReaderOptions.SkipComments
                        if (first == JsonConstants.Solidus)
                        {
                            return true;
                        }
                    }
                }

                ThrowJsonReaderException(ref this, ExceptionResource.ExpectedEndAfterSingleJson, first);
            }
            return true;
        }

        private bool ReadMultiSegment()
        {
            bool retVal = false;

            if (_consumed >= (uint)_buffer.Length)
            {
                if (!_isSingleValue && IsLastSpan)
                {
                    if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                        ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                }

                if (!IsLastSpan)
                {
                    if (!GetNextSpan())
                        goto Done;
                    ReadMultiSegment();
                }
                goto Done;
            }

            byte first = _buffer[(int)_consumed];

            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpaceMultiSegment();
                if (_consumed >= (uint)_buffer.Length)
                {
                    if (!_isSingleValue && IsLastSpan)
                    {
                        if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                            ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                    }
                    goto Done;
                }
                first = _buffer[(int)_consumed];
            }

            TokenStartIndex = _consumed;

            if (TokenType == JsonTokenType.None)
            {
                goto ReadFirstToken;
            }

            if (TokenType == JsonTokenType.StartObject)
            {
                if (first == JsonConstants.CloseBrace)
                {
                    _consumed++;
                    _position++;
                    EndObject();
                }
                else
                {
                    if (first != JsonConstants.Quote)
                        ThrowJsonReaderException(ref this);
                    TokenStartIndex++;
                    long prevConsumed = _consumed;
                    long prevPosition = _position;
                    if (ConsumePropertyNameMultiSegment())
                    {
                        return true;
                    }
                    _consumed = prevConsumed;
                    TokenType = JsonTokenType.StartObject;
                    _position = prevPosition;
                    return false;
                }
            }
            else if (TokenType == JsonTokenType.StartArray)
            {
                if (first == JsonConstants.CloseBracket)
                {
                    _consumed++;
                    _position++;
                    EndArray();
                }
                else
                {
                    return ConsumeValueMultiSegment(first);
                }
            }
            else if (TokenType == JsonTokenType.PropertyName)
            {
                return ConsumeValueMultiSegment(first);
            }
            else
            {
                long prevConsumed = _consumed;
                long prevPosition = _position;
                long prevLineNumber = _lineNumber;
                JsonTokenType prevTokenType = TokenType;

                InternalResult result = ConsumeNextTokenMultiSegment(first);
                if (result == InternalResult.Success)
                {
                    return true;
                }
                if (result == InternalResult.FailureRollback)
                {
                    _consumed = prevConsumed;
                    TokenType = prevTokenType;
                    _position = prevPosition;
                    _lineNumber = prevLineNumber;
                }
                return false;
            }

            retVal = true;

        Done:
            return retVal;

        ReadFirstToken:
            retVal = ReadFirstTokenMultiSegment(first);
            goto Done;
        }

        private InternalResult ConsumeNextToken(ref BufferReader<byte> reader, byte marker)
        {
            reader.Advance(1);
            _consumed++;
            _position++;

            if (_readerOptions != JsonReaderOptions.Default)
            {
                if (_readerOptions == JsonReaderOptions.AllowComments)
                {
                    if (marker == JsonConstants.Solidus)
                    {
                        _consumed--;
                        _position--;
                        reader.Rewind(1);
                        return ConsumeComment(ref reader) ? InternalResult.Success : InternalResult.FailureRollback;
                    }
                    if (TokenType == JsonTokenType.Comment)
                    {
                        _consumed--;
                        _position--;
                        reader.Rewind(1);
                        TokenType = _stack.Pop();
                        if (ReadMultiSegment(ref reader))
                            return InternalResult.Success;
                        else
                        {
                            _stack.Push(TokenType);
                            return InternalResult.FailureRollback;
                        }
                    }
                }
                else
                {
                    // JsonReaderOptions.SkipComments
                    if (marker == JsonConstants.Solidus)
                    {
                        _consumed--;
                        _position--;
                        reader.Rewind(1);
                        if (SkipComment(ref reader))
                        {
                            if (!reader.TryPeek(out byte first))
                            {
                                if (!_isSingleValue && _isFinalBlock)
                                {
                                    if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                                        ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                                }
                                return InternalResult.FalseNoRollback;
                            }

                            if (first <= JsonConstants.Space)
                            {
                                SkipWhiteSpaceMultiSegment();
                                if (!reader.TryPeek(out first))
                                {
                                    if (!_isSingleValue && _isFinalBlock)
                                    {
                                        if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                                            ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                                    }
                                    return InternalResult.FalseNoRollback;
                                }
                            }

                            return ConsumeNextToken(ref reader, first);
                        }
                        return InternalResult.FailureRollback;
                    }
                }
            }

            if (marker == JsonConstants.ListSeperator)
            {
                if (!reader.TryPeek(out byte first))
                {
                    if (_isFinalBlock)
                    {
                        _consumed--;
                        _position--;
                        reader.Rewind(1);
                        ThrowJsonReaderException(ref this);
                    }
                    else return InternalResult.FailureRollback;
                }

                if (first <= JsonConstants.Space)
                {
                    SkipWhiteSpaceMultiSegment();
                    // The next character must be a start of a property name or value.
                    if (!reader.TryPeek(out first))
                    {
                        if (_isFinalBlock)
                            ThrowJsonReaderException(ref this);
                        else return InternalResult.FailureRollback;
                    }
                }

                TokenStartIndex = _consumed;
                if (_inObject)
                {
                    if (first != JsonConstants.Quote)
                        ThrowJsonReaderException(ref this);

                    reader.Advance(1);
                    _consumed++;
                    TokenStartIndex++;
                    return ConsumePropertyName(ref reader) ? InternalResult.Success : InternalResult.FailureRollback;
                }
                else
                {
                    return ConsumeValue(ref reader, first) ? InternalResult.Success : InternalResult.FailureRollback;
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
                _consumed--;
                _position--;
                ThrowJsonReaderException(ref this);
            }
            return InternalResult.Success;
        }

        private bool ConsumeValue(ref BufferReader<byte> reader, byte marker)
        {
            if (marker == JsonConstants.Quote)
            {
                long prevConsumed = _consumed;
                long prevPosition = _position;
                reader.Advance(1);
                _consumed++;
                TokenStartIndex++;
                if (!ConsumeString(ref reader))
                {
                    _consumed = prevConsumed;
                    _position = prevPosition;
                    reader.Rewind(1);
                    return false;
                }
                return true;
            }
            else if (marker == JsonConstants.OpenBrace)
            {
                reader.Advance(1);
                _consumed++;
                StartObject();
            }
            else if (marker == JsonConstants.OpenBracket)
            {
                reader.Advance(1);
                _consumed++;
                StartArray();
            }
            else if ((uint)(marker - '0') <= '9' - '0')
            {
                return ConsumeNumber(ref reader);
            }
            else if (marker == '-')
            {
                // Couldn't find anything beyond '-', can't advance past it since we need '-' to be part of the value
                // TODO: This could silently allocate. Try to avoid it.
                if (reader.Peek(2).Length == 1)
                {
                    if (_isFinalBlock)
                    {
                        ThrowJsonReaderException(ref this);
                    }
                    else return false;
                }
                return ConsumeNumber(ref reader);
            }
            else if (marker == 'f')
            {
                return ConsumeFalse(ref reader);
            }
            else if (marker == 't')
            {
                return ConsumeTrue(ref reader);
            }
            else if (marker == 'n')
            {
                return ConsumeNull(ref reader);
            }
            else
            {
                if (_readerOptions != JsonReaderOptions.Default)
                {
                    if (_readerOptions == JsonReaderOptions.AllowComments)
                    {
                        if (marker == JsonConstants.Solidus)
                        {
                            return ConsumeComment(ref reader);
                        }
                    }
                    else
                    {
                        // JsonReaderOptions.SkipComments
                        if (marker == JsonConstants.Solidus)
                        {
                            if (SkipComment(ref reader))
                            {
                                if (!reader.TryPeek(out byte first))
                                {
                                    if (!_isSingleValue && _isFinalBlock)
                                    {
                                        if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                                            ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                                    }
                                    return false;
                                }

                                if (first <= JsonConstants.Space)
                                {
                                    SkipWhiteSpaceMultiSegment();
                                    if (!reader.TryPeek(out first))
                                    {
                                        if (!_isSingleValue && _isFinalBlock)
                                        {
                                            if (TokenType != JsonTokenType.EndArray && TokenType != JsonTokenType.EndObject)
                                                ThrowJsonReaderException(ref this, ExceptionResource.InvalidEndOfJson);
                                        }
                                        return false;
                                    }
                                }
                                return ConsumeValue(ref reader, first);
                            }
                            return false;
                        }
                    }
                }

                ThrowJsonReaderException(ref this);
            }
            return true;
        }

        private bool SkipComment(ref BufferReader<byte> reader)
        {
            //TODO: Re-evaluate recovery mechanism if this fails. Do we need to rewind?
            reader.Advance(1);
            if (reader.TryPeek(out byte marker))
            {
                if (marker == JsonConstants.Solidus)
                {
                    reader.Advance(1);
                    return SkipSingleLineComment(ref reader);
                }
                else if (marker == '*')
                {
                    reader.Advance(1);
                    return SkipMultiLineComment(ref reader);
                }
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

        private bool SkipSingleLineComment(ref BufferReader<byte> reader)
        {
            long length = 0;
            if (!reader.TryReadTo(out ReadOnlySpan<byte> span, JsonConstants.LineFeed, advancePastDelimiter: true))
            {
                if (_isFinalBlock)
                {
                    // Assume everything on this line is a comment and there is no more data.
                    ReadOnlySequence<byte> sequence = reader.Sequence.Slice(reader.Position);
                    length = sequence.Length;

                    _position += 2 + length;

                    reader.Advance(length);
                    goto Done;
                }
                else return false;
            }
            length = span.Length;

            _consumed++;
            _position = 0;
            _lineNumber++;
        Done:
            _consumed += 2 + length;
            return true;
        }

        private bool SkipMultiLineComment(ref BufferReader<byte> reader)
        {
            if (!reader.TryReadTo(out ReadOnlySequence<byte> sequence, JsonConstants.EndOfComment, advancePastDelimiter: true))
            {
                if (_isFinalBlock)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.EndOfCommentNotFound);
                }
                else return false;
            }

            _consumed += 4 + sequence.Length;

            (int newLines, int newLineIndex) = JsonReaderHelper.CountNewLines(sequence);
            _lineNumber += newLines;
            if (newLineIndex != -1)
            {
                _position = sequence.Length - newLineIndex + 1;
            }
            else
            {
                _position += 4 + sequence.Length;
            }
            return true;
        }

        private bool ConsumeComment(ref BufferReader<byte> reader)
        {
            //TODO: Re-evaluate recovery mechanism if this fails. Do we need to rewind?
            reader.Advance(1);
            if (reader.TryPeek(out byte marker))
            {
                if (marker == JsonConstants.Solidus)
                {
                    reader.Advance(1);
                    return ConsumeSingleLineComment(ref reader);
                }
                else if (marker == '*')
                {
                    reader.Advance(1);
                    return ConsumeMultiLineComment(ref reader);
                }
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

        private bool ConsumeSingleLineComment(ref BufferReader<byte> reader)
        {
            if (!reader.TryReadTo(out ReadOnlySpan<byte> span, JsonConstants.LineFeed, advancePastDelimiter: true))
            {
                if (_isFinalBlock)
                {
                    // Assume everything on this line is a comment and there is no more data.
                    ReadOnlySequence<byte> sequence = reader.Sequence.Slice(reader.Position);
                    Value = sequence.IsSingleSegment ? sequence.First.Span : sequence.ToArray();

                    _position += 2 + Value.Length;

                    reader.Advance(Value.Length);
                    goto Done;
                }
                else return false;
            }

            Value = span;
            _consumed++;
            _position = 0;
            _lineNumber++;
        Done:
            _stack.Push(TokenType);
            TokenType = JsonTokenType.Comment;
            _consumed += 2 + Value.Length;
            return true;
        }

        private bool ConsumeMultiLineComment(ref BufferReader<byte> reader)
        {
            if (!reader.TryReadTo(out ReadOnlySequence<byte> sequence, JsonConstants.EndOfComment, advancePastDelimiter: true))
            {
                if (_isFinalBlock)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.EndOfCommentNotFound);
                }
                else return false;
            }

            _stack.Push(TokenType);
            Value = sequence.IsSingleSegment ? sequence.First.Span : sequence.ToArray();
            TokenType = JsonTokenType.Comment;
            _consumed += 4 + Value.Length;

            (int newLines, int newLineIndex) = JsonReaderHelper.CountNewLines(Value);
            _lineNumber += newLines;
            if (newLineIndex != -1)
            {
                _position = Value.Length - newLineIndex + 1;
            }
            else
            {
                _position += 4 + Value.Length;
            }
            return true;
        }

        private bool ConsumeNumber(ref BufferReader<byte> reader)
        {
            long consumedBefore = reader.Consumed;
            if (!reader.TryReadToAny(out ReadOnlySpan<byte> span, JsonConstants.Delimiters, advancePastDelimiter: false))
            {
                if (_isFinalBlock)
                {
                    ThrowJsonReaderException(ref this);
                }
                else return false;
            }
            ValidateNumber(span);
            _consumed += (int)(reader.Consumed - consumedBefore);
            _position += (int)(reader.Consumed - consumedBefore);
            Value = span;
            TokenType = JsonTokenType.Number;
            return true;
        }

        private bool ConsumeNull(ref BufferReader<byte> reader)
        {
            Value = JsonConstants.NullValue;

            Debug.Assert(reader.TryPeek(out byte first) && first == Value[0]);

            if (!reader.IsNext(JsonConstants.NullValue, advancePast: true))
            {
                if (_isFinalBlock)
                {
                    goto Throw;
                }
                else
                {
                    // TODO: Throw if next two bytes are not 'u' and 'l'.
                    // We know first byte is 'n', and if fourth byte was 'l', we wouldn't be here.
                    return false;
                }
            Throw:
                ThrowJsonReaderException(ref this);
            }
            TokenType = JsonTokenType.Null;
            _consumed += 4;
            _position += 4;
            return true;
        }

        private bool ConsumeFalse(ref BufferReader<byte> reader)
        {
            Value = JsonConstants.FalseValue;

            Debug.Assert(reader.TryPeek(out byte first) && first == Value[0]);

            if (!reader.IsNext(JsonConstants.FalseValue, advancePast: true))
            {
                if (_isFinalBlock)
                {
                    goto Throw;
                }
                else
                {
                    // TODO: Throw if next three bytes are not 'a', 'l', and 's'.
                    // We know first byte is 'f', and if fifth byte was 'e', we wouldn't be here.
                    return false;
                }
            Throw:
                ThrowJsonReaderException(ref this);
            }
            TokenType = JsonTokenType.False;
            _consumed += 5;
            _position += 5;
            return true;
        }

        private bool ConsumeTrue(ref BufferReader<byte> reader)
        {
            Value = JsonConstants.TrueValue;

            Debug.Assert(reader.TryPeek(out byte first) && first == Value[0]);

            if (!reader.IsNext(JsonConstants.TrueValue, advancePast: true))
            {
                if (_isFinalBlock)
                {
                    goto Throw;
                }
                else
                {
                    // TODO: Throw if next three bytes are not 'r' and 'u'.
                    // We know first byte is 't', and if fourth byte was 'e', we wouldn't be here.
                    return false;
                }
            Throw:
                ThrowJsonReaderException(ref this);
            }
            TokenType = JsonTokenType.True;
            _consumed += 4;
            _position += 4;
            return true;
        }

        private bool ConsumePropertyNameMultiSegment()
        {
            if (!ConsumeStringMultiSegment())
                return false;

            //Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localCopy = _buffer;
            if (_consumed >= (uint)localCopy.Length)
            {
                if (_isFinalBlock)
                {

                    ThrowJsonReaderException(ref this);
                }
                else return false;
            }

            byte first = localCopy[(int)_consumed];

            if (first <= JsonConstants.Space)
            {
                SkipWhiteSpaceMultiSegment();
                if (_consumed >= (uint)localCopy.Length)
                {
                    if (_isFinalBlock)
                    {

                        ThrowJsonReaderException(ref this);
                    }
                    else return false;
                }
                first = localCopy[(int)_consumed];
            }

            // The next character must be a key / value seperator. Validate and skip.
            if (first != JsonConstants.KeyValueSeperator)
            {
                ThrowJsonReaderException(ref this);
            }
            
            _consumed++;
            _position++;
            return true;
        }

        private bool ConsumeStringMultiSegment()
        {
            Debug.Assert(_buffer.Length >= _consumed + 1);
            Debug.Assert(_buffer[(int)_consumed] == JsonConstants.Quote);

            return ConsumeStringVectorizedMultiSegment();
        }

        private bool ConsumeStringVectorizedMultiSegment()
        {
            //Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localCopy = _buffer;

            int idx = localCopy.Slice((int)_consumed + 1).IndexOf(JsonConstants.Quote);
            if (idx < 0)
            {
                if (IsLastSpan)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.EndOfStringNotFound);
                }
                else return false;
            }

            if (localCopy[idx + (int)_consumed] != JsonConstants.ReverseSolidus)
            {
                localCopy = localCopy.Slice((int)_consumed + 1, idx);

                if (localCopy.IndexOfAnyControlOrEscape() != -1)
                {
                    _position++;
                    if (ValidateEscaping_AndHex(localCopy))
                        goto Done;
                    return false;
                }

                _position += idx + 1;

            Done:
                _position++;
                Value = localCopy;
                TokenType = JsonTokenType.String;
                _consumed += idx + 2;
                return true;
            }
            else
            {
                return ConsumeStringWithNestedQuotesMultiSegment();
            }
        }

        private void SkipWhiteSpaceMultiSegment()
        {
            ReadOnlySpan<byte> localCopy = _buffer;
            for (; _consumed < localCopy.Length; _consumed++)
            {
                byte val = localCopy[(int)_consumed];
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
                    _position = 0;
                }
                else
                {
                    _position++;
                }
            }
            if (_consumed == localCopy.Length && !IsLastSpan)
            {
                if (!GetNextSpan())
                    return;
                SkipWhiteSpaceMultiSegment();
            }
        }

        private bool GetNextSpan()
        {
            bool result = false;
            while (true)
            {
                SequencePosition previousNextPosition = _nextPosition;
                if (_data.TryGet(ref _nextPosition, out ReadOnlyMemory<byte> memory, advance: true))
                {
                    if (memory.Length > 0)
                    {
                        _buffer = memory.Span;
                        _consumed = 0;
                        result = true;
                        break;
                    }
                }
                else
                {
                    _nextPosition = previousNextPosition;
                    break;
                }
            }
            if (_isFinalBlock)
                _isLastSegment = !_data.TryGet(ref _nextPosition, out _, advance: false);
            return result;
        }

        // https://tools.ietf.org/html/rfc7159#section-6
        // TODO: Avoid code duplication with TryGetNumber
        private void ValidateNumber(ReadOnlySpan<byte> data)
        {
            Debug.Assert(data.Length > 0);

            ReadOnlySpan<byte> delimiters = JsonConstants.Delimiters;

            int i = 0;
            byte nextByte = data[i];

            if (nextByte == '-')
            {
                i++;
                if (i >= data.Length)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
                }

                nextByte = data[i];
                if ((uint)(nextByte - '0') > '9' - '0')
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFound, nextByte);
            }

            Debug.Assert(nextByte >= '0' && nextByte <= '9');

            if (nextByte == '0')
            {
                i++;
                if (i >= data.Length)
                {
                    goto Done;
                }
                else
                {
                    nextByte = data[i];

                    if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                        ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
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
                    goto Done;
                }
                if (nextByte != '.' && nextByte != 'E' && nextByte != 'e')
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitComponentNotFound, nextByte);
            }

            Debug.Assert(nextByte == '.' || nextByte == 'E' || nextByte == 'e');

            if (nextByte == '.')
            {
                i++;
                if (i >= data.Length)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
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
                    goto Done;
                }
                if (nextByte != 'E' && nextByte != 'e')
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedNextDigitEValueNotFound, nextByte);
            }

            Debug.Assert(nextByte == 'E' || nextByte == 'e');
            i++;

            if (i >= data.Length)
            {
                ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
            }

            nextByte = data[i];
            if (nextByte == '+' || nextByte == '-')
            {
                i++;
                if (i >= data.Length)
                {
                    ThrowJsonReaderException(ref this, ExceptionResource.ExpectedDigitNotFoundEndOfData, nextByte);
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

        Done:
            ;
        }
    }
}
