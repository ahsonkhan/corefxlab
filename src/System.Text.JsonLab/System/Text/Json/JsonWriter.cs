﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Buffers.Text;
using System.Buffers.Writer;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Text.JsonLab
{
    public struct JsonWriterOptions
    {
        // Or Minified, Minimzed, PrettyPrinted,
        public bool Formatted { get; set; }

        public bool SkipValidation { get; set; }

        internal bool SlowPath => Formatted || !SkipValidation;
    }

    public struct BitStack
    {
        // We are using a ulong to represent our nested state, so we can only
        // go 64 levels deep without having to allocate.
        private const int AllocationFreeMaxDepth = sizeof(ulong) * 8;

        private const int DefaultInitialArraySize = 2;

        private int[] _array;

        // This ulong container represents a tiny stack to track the state during nested transitions.
        // The first bit represents the state of the current depth (1 == object, 0 == array).
        // Each subsequent bit is the parent / containing type (object or array). Since this
        // reader does a linear scan, we only need to keep a single path as we go through the data.
        // This is primarily used as an optimization to avoid having to allocate an object for
        // depths up to 64 (which is the default max depth).
        private ulong _allocationFreeContainer;

        private int _currentDepth;

        public int CurrentDepth => _currentDepth;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushTrue()
        {
            if (_currentDepth < AllocationFreeMaxDepth)
            {
                _allocationFreeContainer = (_allocationFreeContainer << 1) | 1;
            }
            else
            {
                PushToArray(true);
            }
            _currentDepth++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushFalse()
        {
            if (_currentDepth < AllocationFreeMaxDepth)
            {
                _allocationFreeContainer = _allocationFreeContainer << 1;
            }
            else
            {
                PushToArray(false);
            }
            _currentDepth++;
        }

        // Allocate the bit array lazily only when it is absolutely necessary
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PushToArray(bool value)
        {
            if (_array == null)
            {
                _array = new int[DefaultInitialArraySize];
            }

            int index = _currentDepth - AllocationFreeMaxDepth;

            Debug.Assert(index >= 0, $"Set - Negative - index: {index}, arrayLength: {_array.Length}");

            // Maximum possible array length if bitLength was int.MaxValue (i.e. 67_108_864)
            Debug.Assert(_array.Length <= int.MaxValue / 32 + 1, $"index: {index}, arrayLength: {_array.Length}");

            int elementIndex = Div32Rem(index, out int extraBits);

            // Grow the array when setting a bit if it isn't big enough
            // This way the caller doesn't have to check.
            if (elementIndex >= _array.Length)
            {
                // This multiplication can overflow, so cast to uint first.
                Debug.Assert(index >= 0 && index > (int)((uint)_array.Length * 32 - 1), $"Only grow when necessary - index: {index}, arrayLength: {_array.Length}");
                DoubleArray(elementIndex);
            }

            Debug.Assert(elementIndex < _array.Length, $"Set - index: {index}, elementIndex: {elementIndex}, arrayLength: {_array.Length}, extraBits: {extraBits}");

            int newValue = _array[elementIndex];
            if (value)
            {
                newValue |= 1 << extraBits;
            }
            else
            {
                newValue &= ~(1 << extraBits);
            }
            _array[elementIndex] = newValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Pop()
        {
            _currentDepth--;
            bool inObject = false;
            if (_currentDepth < AllocationFreeMaxDepth)
            {
                _allocationFreeContainer >>= 1;
                inObject = (_allocationFreeContainer & 1) != 0;
            }
            else if (_currentDepth == AllocationFreeMaxDepth)
            {
                inObject = (_allocationFreeContainer & 1) != 0;
            }
            else
            {
                inObject = PopFromArray();
            }
            return inObject;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool PopFromArray()
        {
            int index = _currentDepth - AllocationFreeMaxDepth - 1;
            Debug.Assert(_array != null);
            Debug.Assert(index >= 0, $"Get - Negative - index: {index}, arrayLength: {_array.Length}");

            int elementIndex = Div32Rem(index, out int extraBits);

            Debug.Assert(elementIndex < _array.Length, $"Get - index: {index}, elementIndex: {elementIndex}, arrayLength: {_array.Length}, extraBits: {extraBits}");

            return (_array[elementIndex] & (1 << extraBits)) != 0;
        }

        private void DoubleArray(int minSize)
        {
            Debug.Assert(_array.Length < int.MaxValue / 2, $"Array too large - arrayLength: {_array.Length}");
            Debug.Assert(minSize >= 0 && minSize >= _array.Length);

            int nextDouble = Math.Max(minSize + 1, _array.Length * 2);
            Debug.Assert(nextDouble > minSize);

            Array.Resize(ref _array, nextDouble);
        }

        public void SetFirstBit()
        {
            Debug.Assert(_currentDepth == 0, "Only call SetFirstBit when depth is 0");
            _currentDepth++;
            _allocationFreeContainer = 1;
        }

        public void ResetFirstBit()
        {
            Debug.Assert(_currentDepth == 0, "Only call ResetFirstBit when depth is 0");
            _currentDepth++;
            _allocationFreeContainer = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Div32Rem(int number, out int remainder)
        {
            uint quotient = (uint)number / 32;
            remainder = number & (32 - 1);   // equivalent to number % 32, since 32 is a power of 2
            return (int)quotient;
        }
    }

    public sealed class JsonWriterException : Exception
    {
        public JsonWriterException(string message) : base(message)
        {
        }
    }

    public ref struct Utf8JsonWriter2<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
    {
        private readonly JsonWriterOptions _options;
        private BufferWriter<TBufferWriter> _bufferWriter;
        private JsonTokenType _tokenType;
        private bool _inObject;
        private BitStack _bitStack;

        // The highest order bit of _indent is used to discern whether we are writing the first item in a list or not.
        // if (_indent >> 31) == 1, add a list separator before writing the item
        // else, no list separator is needed since we are writing the first item.
        private int _indent;

        private const int RemoveFlagsBitMask = 0x7FFFFFFF;
        private const int MaxDepth = (int.MaxValue - 2_000_001_000) / 2;  // 73_741_323 (to account for double space indentation)
        private const int MaxTokenSize = 1_000_000_000; // 1 GB
        private const int MaxCharacterTokenSize = 1_000_000_000 / 3 * 2; // 333 million characters, i.e. 666 MB

        /// <summary>
        /// Constructs a JSON writer with a specified <paramref name="bufferWriter"/>.
        /// </summary>
        /// <param name="bufferWriter">An instance of <see cref="ITextBufferWriter" /> used for writing bytes to an output channel.</param>
        /// <param name="prettyPrint">Specifies whether to add whitespace to the output text for user readability.</param>
        public Utf8JsonWriter2(TBufferWriter bufferWriter, JsonWriterOptions options = default)
        {
            _bufferWriter = new BufferWriter<TBufferWriter>(bufferWriter);
            _options = options;
            _indent = 0;
            _tokenType = JsonTokenType.None;
            _inObject = false;
            _bitStack = default;
        }

        public void Flush() => _bufferWriter.Flush();

        public void WriteStartArray()
        {
            WriteStart(JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject()
        {
            WriteStart(JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartObject;
        }

        private void WriteStart(byte token)
        {
            // TODO: Use throw helper with proper error messages
            if ((_indent & RemoveFlagsBitMask) >= MaxDepth)
                JsonThrowHelper.ThrowJsonWriterException("Depth too large.");

            if (_options.SlowPath)
                WriteStartSlow(token);
            else
                WriteStartFast(token);

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        private void WriteStartFast(byte token)
        {
            // Calculated based on the following: ',[' OR ',{'
            int bytesNeeded = 2;
            if (_indent < 0)
            {
                Span<byte> byteBuffer = GetSpan(bytesNeeded);
                byteBuffer[0] = JsonConstants.ListSeperator;
                byteBuffer[1] = token;
                _bufferWriter.Advance(bytesNeeded);
            }
            else
            {
                bytesNeeded--;
                Span<byte> byteBuffer = GetSpan(bytesNeeded);
                byteBuffer[0] = token;
                _bufferWriter.Advance(bytesNeeded);
            }
        }

        private void WriteStartSlow(byte token)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateStart(token);
                }
                WriteStartFormatted(token);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateStart(token);
                WriteStartFast(token);
            }
        }

        private void ValidateStart(byte token)
        {
            if (_inObject)
            {
                Debug.Assert(_tokenType != JsonTokenType.None && _tokenType != JsonTokenType.StartArray);
                if (_tokenType != JsonTokenType.PropertyName)
                {
                    JsonThrowHelper.ThrowJsonWriterException(token, _tokenType);    //TODO: Add resource message
                }
            }
            else
            {
                Debug.Assert(_tokenType != JsonTokenType.StartObject);
                if (_tokenType == JsonTokenType.PropertyName)
                {
                    JsonThrowHelper.ThrowJsonWriterException(token, _tokenType);    //TODO: Add resource message
                }
            }

            UpdateBitStackOnStart(token);
        }

        private void WriteStartFormatted(byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - JsonWriterHelper.NewLineUtf8.Length - 2 - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  [' OR ',\r\n  {'
            int bytesNeeded = JsonWriterHelper.NewLineUtf8.Length + 2 + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx] = token;

            _bufferWriter.Advance(bytesNeeded);
        }

        public void WriteStartArray(ReadOnlySpan<byte> propertyName)
        {
            WriteStart(propertyName, JsonConstants.OpenBracket);
            _tokenType = JsonTokenType.StartArray;
        }

        public void WriteStartObject(ReadOnlySpan<byte> propertyName)
        {
            WriteStart(propertyName, JsonConstants.OpenBrace);
            _tokenType = JsonTokenType.StartArray;
        }

        private void WriteStart(ReadOnlySpan<byte> propertyName, byte token)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxTokenSize || (_indent & RemoveFlagsBitMask) >= MaxDepth)
                JsonThrowHelper.ThrowJsonWriterException("Depth too large.");

            if (_options.SlowPath)
                WriteStartSlow(propertyName, token);
            else
                WriteStartFast(propertyName, token);

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        private void WriteStartFast(ReadOnlySpan<byte> propertyName, byte token)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - 5 >= 0);

            // Calculated based on the following: ',"propertyName":[' OR ',"propertyName":{'
            int bytesNeeded = propertyName.Length + 5;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = token;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStartSlow(ReadOnlySpan<byte> propertyName, byte token)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateStart(propertyName, token);
                }
                WriteStartFormatted(propertyName, token);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateStart(propertyName, token);
                WriteStartFast(propertyName, token);
            }
        }

        private void ValidateStart(ReadOnlySpan<byte> propertyName, byte token)
        {
            if (JsonWriterHelper.IndexOfAnyEscape(propertyName) != -1)
                JsonThrowHelper.ThrowJsonWriterException("Property name must be properly escaped."); //TODO: Fix message

            if (!_inObject)
            {
                Debug.Assert(_tokenType != JsonTokenType.StartObject);
                JsonThrowHelper.ThrowJsonWriterException(token);    //TODO: Add resouce message
            }

            UpdateBitStackOnStart(token);
        }

        private void WriteStartFormatted(ReadOnlySpan<byte> propertyName, byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - JsonWriterHelper.NewLineUtf8.Length - 6 - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "propertyName": [' OR ',\r\n  "propertyName": {'
            int bytesNeeded = propertyName.Length + 6 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;
            byteBuffer[idx] = token;

            _bufferWriter.Advance(bytesNeeded);
        }

        public void WriteStartArray(string propertyName)
            => WriteStartArray(propertyName.AsSpan());

        public void WriteStartObject(string propertyName)
            => WriteStartObject(propertyName.AsSpan());

        public void WriteStartArray(ReadOnlySpan<char> propertyName)
            => WriteStartArrayWithEncoding(MemoryMarshal.AsBytes(propertyName));

        public void WriteStartObject(ReadOnlySpan<char> propertyName)
            => WriteStartObjectWithEncoding(MemoryMarshal.AsBytes(propertyName));

        private void WriteStartArrayWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize || (_indent & RemoveFlagsBitMask) >= MaxDepth)
                JsonThrowHelper.ThrowJsonWriterException("Depth too large.");

            if (_options.SlowPath)
                WriteStartSlowWithEncoding(propertyName, JsonConstants.OpenBracket);
            else
                WriteStartFastWithEncoding(propertyName, JsonConstants.OpenBracket);

            _indent &= RemoveFlagsBitMask;
            _indent++;
            _tokenType = JsonTokenType.StartArray;
        }

        private void WriteStartObjectWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize || (_indent & RemoveFlagsBitMask) >= MaxDepth)
                JsonThrowHelper.ThrowJsonWriterException("Depth too large.");

            if (_options.SlowPath)
                WriteStartSlowWithEncoding(propertyName, JsonConstants.OpenBrace);
            else
                WriteStartFastWithEncoding(propertyName, JsonConstants.OpenBrace);

            _indent &= RemoveFlagsBitMask;
            _indent++;
            _tokenType = JsonTokenType.StartObject;
        }

        private void WriteStartFastWithEncoding(ReadOnlySpan<byte> propertyName, byte token)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - 5 >= 0);

            // Calculated based on the following: ',"encoded propertyName":[' OR ',"encoded propertyName":{'
            int bytesNeeded = propertyName.Length / 2 * 3 + 5;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = token;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStartSlowWithEncoding(ReadOnlySpan<byte> propertyName, byte token)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateStart(propertyName, token);
                }
                WriteStartFormattedWithEncoding(propertyName, token);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateStart(propertyName, token);
                WriteStartFast(propertyName, token);
            }
        }

        private void WriteStartFormattedWithEncoding(ReadOnlySpan<byte> propertyName, byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - JsonWriterHelper.NewLineUtf8.Length - 6 - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "encoded propertyName": [' OR ',\r\n  "encoded propertyName": {'
            int bytesNeeded = propertyName.Length / 2 * 3 + 6 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;
            byteBuffer[idx] = token;

            _bufferWriter.Advance(bytesNeeded);
        }

        public void WriteEndArray()
        {
            WriteEnd(JsonConstants.CloseBracket);
            _tokenType = JsonTokenType.EndArray;
        }

        public void WriteEndObject()
        {
            WriteEnd(JsonConstants.CloseBrace);
            _tokenType = JsonTokenType.EndObject;
        }

        private void WriteEnd(byte token)
        {
            _indent |= 1 << 31;
            _indent--;

            if (_options.SlowPath)
                WriteEndSlow(token);
            else
                WriteEndFast(token);
        }

        private void WriteEndFast(byte token)
        {
            Span<byte> byteBuffer = GetSpan(1);
            byteBuffer[0] = token;
            _bufferWriter.Advance(1);
        }

        private void WriteEndSlow(byte token)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateEnd(token);
                }
                WriteEndFormatted(token);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateEnd(token);
                WriteEndFast(token);
            }
        }

        private void ValidateEnd(byte token)
        {
            if (_bitStack.CurrentDepth <= 0)
                JsonThrowHelper.ThrowJsonWriterException(token);    //TODO: Add resource message

            if (token == JsonConstants.CloseBracket)
            {
                if (_inObject)
                {
                    Debug.Assert(_tokenType != JsonTokenType.None);
                    JsonThrowHelper.ThrowJsonWriterException(token);    //TODO: Add resource message
                }
            }
            else
            {
                Debug.Assert(token == JsonConstants.CloseBrace);

                if (!_inObject)
                {
                    JsonThrowHelper.ThrowJsonWriterException(token);    //TODO: Add resource message
                }
            }

            _inObject = _bitStack.Pop();
        }

        private void WriteEndFormatted(byte token)
        {
            // Do not format/indent empty JSON object/array.
            if ((token == JsonConstants.CloseBrace && _tokenType == JsonTokenType.StartObject)
                || (token == JsonConstants.CloseBracket && _tokenType == JsonTokenType.StartArray))
            {
                WriteEndFast(token);
            }
            else
            {
                // Necessary if WriteEndX is called without a corresponding WriteStartX first.
                // Checking for int.MaxValue because int.MinValue - 1 = int.MaxValue
                if (_indent == int.MaxValue)
                {
                    _indent = 0;
                }

                int indent = _indent & RemoveFlagsBitMask;

                // This is guaranteed not to overflow.
                Debug.Assert(int.MaxValue - JsonWriterHelper.NewLineUtf8.Length - 1 - indent * 2 >= 0);

                // For new line (\r\n or \n), indentation (based on depth) and end token ('}' or ']').
                int bytesNeeded = JsonWriterHelper.NewLineUtf8.Length + 1 + indent * 2;

                _bufferWriter.Ensure(bytesNeeded);
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                int idx = 0;

                WriteNewLine(byteBuffer, ref idx);
                WriteIndentation(byteBuffer, ref idx, indent);

                byteBuffer[idx] = token;

                _bufferWriter.Advance(bytesNeeded);
            }
        }

        public void WriteNull(string propertyName)
            => WriteNull(propertyName.AsSpan());

        public void WriteNull(ReadOnlySpan<char> propertyName)
            => WriteNullWithEncoding(MemoryMarshal.AsBytes(propertyName));

        public void WriteNullWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Argument too large.");

            if (_options.SlowPath)
                WriteNullSlowWithEncoding(propertyName);
            else
                WriteNullFastWithEncoding(propertyName);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.Null;
        }

        private void WriteNullFastWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - 8 >= 0);

            // Calculated based on the following: ',"encoded propertyName":null'
            int bytesNeeded = propertyName.Length / 2 * 3 + 8;

            if (_indent >= 0)
                bytesNeeded--;

            ReadOnlySpan<byte> valueSpan = JsonConstants.NullValue;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteNullSlowWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingPropertyWithEncoding(propertyName);
                }
                WriteNullFormattedWithEncoding(propertyName);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingPropertyWithEncoding(propertyName);
                WriteNullFastWithEncoding(propertyName);
            }
        }

        private void WriteNullFormattedWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - 9 - indent * 2 - JsonWriterHelper.NewLineUtf8.Length >= 0);

            // Calculated based on the following: ',\r\n  "encoded propertyName": null'
            int bytesNeeded = propertyName.Length / 2 * 3 + 9 + indent * 2 + JsonWriterHelper.NewLineUtf8.Length;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            ReadOnlySpan<byte> valueSpan = JsonConstants.NullValue;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        public void WriteNull(ReadOnlySpan<byte> propertyName)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Argument too large.");

            if (_options.SlowPath)
                WriteNullSlow(propertyName);
            else
                WriteNullFast(propertyName);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.Null;
        }

        private void WriteNullFast(ReadOnlySpan<byte> propertyName)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - 8 >= 0);

            // Calculated based on the following: ',"propertyName":null'
            int bytesNeeded = propertyName.Length + 8;

            if (_indent >= 0)
                bytesNeeded--;

            ReadOnlySpan<byte> valueSpan = JsonConstants.NullValue;
            _tokenType = JsonTokenType.False;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteNullSlow(ReadOnlySpan<byte> propertyName)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingProperty(propertyName);
                }
                WriteNullFormatted(propertyName);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingProperty(propertyName);
                WriteNullFast(propertyName);
            }
        }

        private void WriteNullFormatted(ReadOnlySpan<byte> propertyName)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - 9 - JsonWriterHelper.NewLineUtf8.Length - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "propertyName": null'
            int bytesNeeded = propertyName.Length + 9 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            ReadOnlySpan<byte> valueSpan = JsonConstants.NullValue;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        public void WriteBoolean(string propertyName, bool value)
            => WriteBoolean(propertyName.AsSpan(), value);

        public void WriteBoolean(ReadOnlySpan<char> propertyName, bool value)
            => WriteBooleanWithEncoding(MemoryMarshal.AsBytes(propertyName), value);

        public void WriteBooleanWithEncoding(ReadOnlySpan<byte> propertyName, bool value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Argument too large.");

            if (_options.SlowPath)
                WriteBooleanSlowWithEncoding(propertyName, value);
            else
                WriteBooleanFastWithEncoding(propertyName, value);

            _indent |= 1 << 31;
        }

        private void WriteBooleanFastWithEncoding(ReadOnlySpan<byte> propertyName, bool value)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - 9 >= 0);

            // Calculated based on the following: ',"encoded propertyName":true' OR ',"encoded propertyName":false'
            int bytesNeeded = propertyName.Length /2 * 3 + 9;

            if (_indent >= 0)
                bytesNeeded--;

            ReadOnlySpan<byte> valueSpan = JsonConstants.FalseValue;
            _tokenType = JsonTokenType.False;

            if (value)
            {
                bytesNeeded--;
                valueSpan = JsonConstants.TrueValue;
                _tokenType = JsonTokenType.True;
            }

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteBooleanSlowWithEncoding(ReadOnlySpan<byte> propertyName, bool value)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingPropertyWithEncoding(propertyName);
                }
                WriteBooleanFormattedWithEncoding(propertyName, value);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingPropertyWithEncoding(propertyName);
                WriteBooleanFastWithEncoding(propertyName, value);
            }
        }

        private void WriteBooleanFormattedWithEncoding(ReadOnlySpan<byte> propertyName, bool value)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - 10 - indent * 2 - JsonWriterHelper.NewLineUtf8.Length >= 0);

            // Calculated based on the following: ',\r\n  "encoded propertyName": true' OR ',\r\n  "encoded propertyName": false'
            int bytesNeeded = propertyName.Length / 2 * 3 + 10 + indent * 2 + JsonWriterHelper.NewLineUtf8.Length;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            ReadOnlySpan<byte> valueSpan = JsonConstants.FalseValue;
            _tokenType = JsonTokenType.False;

            if (value)
            {
                bytesNeeded--;
                valueSpan = JsonConstants.TrueValue;
                _tokenType = JsonTokenType.True;
            }

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        public void WriteBoolean(ReadOnlySpan<byte> propertyName, bool value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Argument too large.");

            if (_options.SlowPath)
                WriteBooleanSlow(propertyName, value);
            else
                WriteBooleanFast(propertyName, value);

            _indent |= 1 << 31;
        }

        private void WriteBooleanFast(ReadOnlySpan<byte> propertyName, bool value)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - 9 >= 0);

            // Calculated based on the following: ',"propertyName":true' OR ',"propertyName":false'
            int bytesNeeded = propertyName.Length + 9;

            if (_indent >= 0)
                bytesNeeded--;

            ReadOnlySpan<byte> valueSpan = JsonConstants.FalseValue;
            _tokenType = JsonTokenType.False;

            if (value)
            {
                bytesNeeded--;
                valueSpan = JsonConstants.TrueValue;
                _tokenType = JsonTokenType.True;
            }

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteBooleanSlow(ReadOnlySpan<byte> propertyName, bool value)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingProperty(propertyName);
                }
                WriteBooleanFormatted(propertyName, value);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingProperty(propertyName);
                WriteBooleanFast(propertyName, value);
            }
        }

        private void WriteBooleanFormatted(ReadOnlySpan<byte> propertyName, bool value)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - 10 - JsonWriterHelper.NewLineUtf8.Length - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "propertyName": true' OR ',\r\n  "propertyName": false'
            int bytesNeeded = propertyName.Length + 10 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            ReadOnlySpan<byte> valueSpan = JsonConstants.FalseValue;
            _tokenType = JsonTokenType.False;

            if (value)
            {
                bytesNeeded--;
                valueSpan = JsonConstants.TrueValue;
                _tokenType = JsonTokenType.True;
            }

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            valueSpan.CopyTo(byteBuffer.Slice(idx));
            idx += valueSpan.Length;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        public void WriteNumber(ReadOnlySpan<byte> propertyName, long value)
        {

        }

        // TODO: Consider re-factoring to reduce duplication
        private void WriteStringWithEncodingPropertyValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize || value.Length > MaxCharacterTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Arguments too large.");

            //TODO: Add ReadOnlySpan<char> overload to this check
            if (JsonWriterHelper.IndexOfAnyEscape(value) != -1)
                value = EscapeStringValue(value);

            if (_options.SlowPath)
                WriteStringSlowWithEncodingPropertyValue(propertyName, value);
            else
                WriteStringFastWithEncodingPropertyValue(propertyName, value);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.String;
        }

        private void WriteStringFastWithEncodingPropertyValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - escapedValue.Length / 2 * 3 - 6 >= 0);

            // Calculated based on the following: ',"encoded propertyName":"encoded value"'
            int bytesNeeded = propertyName.Length / 2 * 3 + escapedValue.Length / 2 * 3 + 6;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            status = Encodings.Utf16.ToUtf8(escapedValue, byteBuffer.Slice(idx), out consumed, out written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == escapedValue.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringSlowWithEncodingPropertyValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingPropertyWithEncoding(propertyName);
                }
                WriteStringFormattedWithEncodingPropertyValue(propertyName, escapedValue);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingPropertyWithEncoding(propertyName);
                WriteStringFastWithEncodingPropertyValue(propertyName, escapedValue);
            }
        }

        private void ValidateWritingPropertyWithEncoding(ReadOnlySpan<byte> propertyName)
        {
            // TODO: Add "char" like escape check
            if (JsonWriterHelper.IndexOfAnyEscape(propertyName) != -1)
            {
                //JsonThrowHelper.ThrowJsonWriterException("Property name must be properly escaped."); //TODO: Fix message
            }

            if (!_inObject)
            {
                Debug.Assert(_tokenType != JsonTokenType.StartObject);
                JsonThrowHelper.ThrowJsonWriterException("Cannot add a property within an array.");    //TODO: Add resouce message
            }
        }

        private void WriteStringFormattedWithEncodingPropertyValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            int indent = _indent &RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - escapedValue.Length / 2 * 3 - 7 - JsonWriterHelper.NewLineUtf8.Length - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "encoded propertyName": "encoded value"'
            int bytesNeeded = propertyName.Length / 2 * 3 + escapedValue.Length / 2 * 3 + 7 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = JsonConstants.Quote;

            status = Encodings.Utf16.ToUtf8(escapedValue, byteBuffer.Slice(idx), out consumed, out written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == escapedValue.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringWithEncodingProperty(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxCharacterTokenSize || value.Length > MaxTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Arguments too large.");

            //TODO: Add ReadOnlySpan<char> overload to this check
            if (JsonWriterHelper.IndexOfAnyEscape(value) != -1)
                value = EscapeStringValue(value);

            if (_options.SlowPath)
                WriteStringSlowWithEncodingProperty(propertyName, value);
            else
                WriteStringFastWithEncodingProperty(propertyName, value);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.String;
        }

        private void WriteStringFastWithEncodingProperty(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - escapedValue.Length - 6 >= 0);

            // Calculated based on the following: ',"encoded propertyName":"value"'
            int bytesNeeded = propertyName.Length / 2 * 3 + escapedValue.Length + 6;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            escapedValue.CopyTo(byteBuffer.Slice(idx));
            idx += escapedValue.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringSlowWithEncodingProperty(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingPropertyWithEncoding(propertyName);
                }
                WriteStringFormattedWithEncodingProperty(propertyName, escapedValue);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingPropertyWithEncoding(propertyName);
                WriteStringFastWithEncodingProperty(propertyName, escapedValue);
            }
        }

        private void WriteStringFormattedWithEncodingProperty(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length / 2 * 3 - escapedValue.Length - 7 - JsonWriterHelper.NewLineUtf8.Length - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "encoded propertyName": "value"'
            int bytesNeeded = propertyName.Length / 2 * 3 + escapedValue.Length + 7 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(propertyName, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == propertyName.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = JsonConstants.Quote;
            escapedValue.CopyTo(byteBuffer.Slice(idx));
            idx += escapedValue.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringWithEncodingValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxTokenSize || value.Length > MaxCharacterTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Arguments too large.");

            //TODO: Add ReadOnlySpan<char> overload to this check
            if (JsonWriterHelper.IndexOfAnyEscape(value) != -1)
                value = EscapeStringValue(value);

            if (_options.SlowPath)
                WriteStringSlowWithEncodingValue(propertyName, value);
            else
                WriteStringFastWithEncodingValue(propertyName, value);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.String;
        }

        private void WriteStringFastWithEncodingValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - escapedValue.Length / 2 * 3 - 6 >= 0);

            // Calculated based on the following: ',"propertyName":"encoded value"'
            int bytesNeeded = propertyName.Length + escapedValue.Length / 2 * 3 + 6;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(escapedValue, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == escapedValue.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringSlowWithEncodingValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingProperty(propertyName);
                }
                WriteStringFormattedWithEncodingValue(propertyName, escapedValue);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingProperty(propertyName);
                WriteStringFastWithEncodingValue(propertyName, escapedValue);
            }
        }

        private void WriteStringFormattedWithEncodingValue(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - escapedValue.Length / 2 * 3 - 7 - JsonWriterHelper.NewLineUtf8.Length - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "propertyName": "encoded value"'
            int bytesNeeded = propertyName.Length + escapedValue.Length / 2 * 3 + 7 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(escapedValue, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(status != OperationStatus.DestinationTooSmall);

            if (status != OperationStatus.Done)
                JsonThrowHelper.ThrowFormatException();

            Debug.Assert(consumed == escapedValue.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx <= bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        public void WriteString(string propertyName, string value)
            => WriteString(propertyName.AsSpan(), value.AsSpan());

        public void WriteString(string propertyName, ReadOnlySpan<char> value)
            => WriteString(propertyName.AsSpan(), value);

        public void WriteString(string propertyName, ReadOnlySpan<byte> value)
            => WriteString(propertyName.AsSpan(), value);

        public void WriteString(ReadOnlySpan<char> propertyName, string value)
            => WriteString(propertyName, value.AsSpan());

        public void WriteString(ReadOnlySpan<char> propertyName, ReadOnlySpan<char> value)
            => WriteStringWithEncodingPropertyValue(MemoryMarshal.AsBytes(propertyName), MemoryMarshal.AsBytes(value));

        public void WriteString(ReadOnlySpan<char> propertyName, ReadOnlySpan<byte> value)
            => WriteStringWithEncodingProperty(MemoryMarshal.AsBytes(propertyName), value);

        public void WriteString(ReadOnlySpan<byte> propertyName, string value)
            => WriteString(propertyName, value.AsSpan());

        public void WriteString(ReadOnlySpan<byte> propertyName, ReadOnlySpan<char> value)
            => WriteStringWithEncodingValue(propertyName, MemoryMarshal.AsBytes(value));

        public void WriteString(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> value)
        {
            // TODO: Use throw helper with proper error messages
            if (propertyName.Length > MaxTokenSize || value.Length > MaxTokenSize)
                JsonThrowHelper.ThrowJsonWriterException("Arguments too large.");

            if (JsonWriterHelper.IndexOfAnyEscape(value) != -1)
            {
                //TODO: Add escaping.
                value = EscapeStringValue(value);
            }

            if (_options.SlowPath)
                WriteStringSlow(propertyName, value);
            else
                WriteStringFast(propertyName, value);

            _indent |= 1 << 31;
            _tokenType = JsonTokenType.String;
        }

        private ReadOnlySpan<byte> EscapeStringValue(ReadOnlySpan<byte> value)
        {
            //TODO: Add escaping logic.
            return value;
        }

        private void WriteStringFast(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - propertyName.Length - escapedValue.Length - 6 >= 0);

            // Calculated based on the following: ',"propertyName":"value"'
            int bytesNeeded = propertyName.Length + escapedValue.Length + 6;

            if (_indent >= 0)
                bytesNeeded--;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Quote;
            escapedValue.CopyTo(byteBuffer.Slice(idx));
            idx += escapedValue.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        private void WriteStringSlow(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            Debug.Assert(_options.Formatted || !_options.SkipValidation);

            if (_options.Formatted)
            {
                if (!_options.SkipValidation)
                {
                    ValidateWritingProperty(propertyName);
                }
                WriteStringFormatted(propertyName, escapedValue);
            }
            else
            {
                Debug.Assert(!_options.SkipValidation);
                ValidateWritingProperty(propertyName);
                WriteStringFast(propertyName, escapedValue);
            }
        }

        private void ValidateWritingProperty(ReadOnlySpan<byte> propertyName)
        {
            if (JsonWriterHelper.IndexOfAnyEscape(propertyName) != -1)
                JsonThrowHelper.ThrowJsonWriterException("Property name must be properly escaped."); //TODO: Fix message

            if (!_inObject)
            {
                Debug.Assert(_tokenType != JsonTokenType.StartObject);
                JsonThrowHelper.ThrowJsonWriterException("Cannot add a property within an array.");    //TODO: Add resouce message
            }
        }

        private void WriteStringFormatted(ReadOnlySpan<byte> propertyName, ReadOnlySpan<byte> escapedValue)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - JsonWriterHelper.NewLineUtf8.Length - propertyName.Length - escapedValue.Length - 7 - indent * 2 >= 0);

            // Calculated based on the following: ',\r\n  "propertyName": "value"'
            int bytesNeeded = propertyName.Length + 7 + JsonWriterHelper.NewLineUtf8.Length + indent * 2 + escapedValue.Length;

            if (_indent >= 0)
                bytesNeeded--;

            if (_tokenType == JsonTokenType.None)
                bytesNeeded -= JsonWriterHelper.NewLineUtf8.Length;

            Span<byte> byteBuffer = GetSpan(bytesNeeded);

            int idx = 0;

            if (_indent < 0)
                byteBuffer[idx++] = JsonConstants.ListSeperator;

            if (_tokenType != JsonTokenType.None)
                WriteNewLine(byteBuffer, ref idx);

            WriteIndentation(byteBuffer, ref idx, indent);

            byteBuffer[idx++] = JsonConstants.Quote;
            propertyName.CopyTo(byteBuffer.Slice(idx));
            idx += propertyName.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = JsonConstants.Quote;
            escapedValue.CopyTo(byteBuffer.Slice(idx));
            idx += escapedValue.Length;
            byteBuffer[idx++] = JsonConstants.Quote;

            Debug.Assert(idx == bytesNeeded);

            _bufferWriter.Advance(idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> GetSpan(int bytesNeeded)
        {
            _bufferWriter.Ensure(bytesNeeded);
            Debug.Assert(_bufferWriter.Buffer.Length >= bytesNeeded);
            return _bufferWriter.Buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteNewLine(Span<byte> byteBuffer, ref int idx)
        {
            // Write '\r\n' OR '\n', depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
                byteBuffer[idx++] = JsonConstants.CarriageReturn;

            byteBuffer[idx++] = JsonConstants.LineFeed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteIndentation(Span<byte> byteBuffer, ref int idx, int indent)
        {
            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateBitStackOnStart(byte token)
        {
            if (token == JsonConstants.OpenBracket)
            {
                _bitStack.PushFalse();
                _inObject = false;
            }
            else
            {
                Debug.Assert(token == JsonConstants.OpenBrace);
                _bitStack.PushTrue();
                _inObject = true;
            }
        }
    }

    public ref struct Utf8JsonWriter<TBufferWriter> where TBufferWriter : IBufferWriter<byte>
    {
        private readonly bool _prettyPrint;
        private BufferWriter<TBufferWriter> _bufferWriter;

        // The highest order bit of _indent is used to discern whether we are writing the first item in a list or not.
        // if (_indent >> 31) == 1, add a list separator before writing the item
        // else, no list separator is needed since we are writing the first item.
        private int _indent;

        private const int RemoveFlagsBitMask = 0x7FFFFFFF;

        /// <summary>
        /// Constructs a JSON writer with a specified <paramref name="bufferWriter"/>.
        /// </summary>
        /// <param name="bufferWriter">An instance of <see cref="ITextBufferWriter" /> used for writing bytes to an output channel.</param>
        /// <param name="prettyPrint">Specifies whether to add whitespace to the output text for user readability.</param>
        public Utf8JsonWriter(TBufferWriter bufferWriter, bool prettyPrint = false)
        {
            _bufferWriter = BufferWriter.Create(bufferWriter);
            _prettyPrint = prettyPrint;
            _indent = 0;
            _resizeBufferBy = -1;
        }

        public void Flush() => _bufferWriter.Flush();

        private int _resizeBufferBy;

        public void Write(JsonObject jsonObject)
        {
            JsonTokenType type = jsonObject.Type;
            switch (type)
            {
                case JsonTokenType.StartObject:
                    WriteObjectStart();
                    WriteObject(jsonObject);
                    break;
                case JsonTokenType.StartArray:
                    WriteArrayStart();
                    WriteArray(jsonObject);
                    break;
                case JsonTokenType.String:
                case JsonTokenType.PropertyName:
                    ReadOnlySpan<byte> span = jsonObject.GetSpan();
                    while (!TryWriteValueStringAlreadyUtf8(span))
                        EnsureBufferConstant();
                    break;
                default:
                    Debug.Assert(type >= JsonTokenType.Number && type <= JsonTokenType.Null);
                    span = jsonObject.GetSpan();
                    while (!TryWriteValueAlreadyUtf8(span))
                        EnsureBufferConstant();
                    break;
            }
        }

        private void Write(JsonObject jsonObject, ReadOnlySpan<byte> name)
        {
            JsonTokenType type = jsonObject.Type;
            switch (type)
            {
                case JsonTokenType.StartObject:
                    WriteObjectStart(name);
                    WriteObject(jsonObject);
                    break;
                case JsonTokenType.StartArray:
                    WriteArrayStart(name);
                    WriteArray(jsonObject);
                    break;
            }
        }

        private void WriteArray(JsonObject jsonObject)
        {
            if (_resizeBufferBy == -1) _resizeBufferBy = jsonObject.Size;
            foreach (JsonObject child in jsonObject)
            {
                Write(child);
            }
            WriteArrayEnd();
        }

        private void WriteObject(JsonObject jsonObject)
        {
            if (_resizeBufferBy == -1) _resizeBufferBy = jsonObject.Size;
            foreach (JsonObject child in jsonObject)
            {
                DbRow valueRow = child.GetRow();
                if (valueRow.IsSimpleValue)
                {
                    ReadOnlySpan<byte> value = child.GetSpan(valueRow);
                    WriteAttribute(child.PropertyName, value, valueRow.JsonType == JsonType.String);
                }
                else
                {
                    Write(child, child.PropertyName);
                }
            }
            WriteObjectEnd();
        }

        private void WriteAttribute(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value, bool isString)
        {
            if (isString)
                WriteAttributeUtf8(name, value);
            else
                WriteAttributeValueUtf8(name, value);
        }

        /// <summary>
        /// Write the starting tag of an object. This is used for adding an object to an
        /// array of other items. If this is used while inside a nested object, the property
        /// name will be missing and result in invalid JSON.
        /// </summary>
        public void WriteObjectStart()
        {
            if (_prettyPrint)
            {
                WriteStartUtf8Pretty(JsonConstants.OpenBrace);
            }
            else
            {
                int bytesNeeded = 1;

                if (_indent < 0)
                {
                    bytesNeeded = 2;
                }

                _bufferWriter.Ensure(bytesNeeded);
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[0] = JsonConstants.ListSeperator;
                }

                byteBuffer[bytesNeeded - 1] = JsonConstants.OpenBrace;
                _bufferWriter.Advance(bytesNeeded);
            }

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        private void WriteStartUtf8Pretty(byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // This is guaranteed not to overflow.
            Debug.Assert(int.MaxValue - 1 - indent * 2 >= 0);

            int bytesNeeded = 1 + indent * 2;

            if (_indent < 0)
            {
                bytesNeeded++;
            }

            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);

            int idx = 0;
            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }

            byteBuffer[idx] = token;
            _bufferWriter.Advance(bytesNeeded);
        }

        private void WriteStartUtf8(int bytesNeeded, byte token)
        {
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);

            if (_indent < 0)
            {
                byteBuffer[0] = JsonConstants.ListSeperator;
            }

            byteBuffer[bytesNeeded - 1] = token;
            _bufferWriter.Advance(bytesNeeded);
        }

        /// <summary>
        /// Write the starting tag of an object. This is used for adding an object to a
        /// nested object. If this is used while inside a nested array, the property
        /// name will be written and result in invalid JSON.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        public void WriteObjectStart(string name)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());

            if (_prettyPrint)
            {
                WriteStartUtf16Pretty(nameSpan, JsonConstants.OpenBrace);
            }
            else
            {
                while (!TryWriteStartUtf16(nameSpan, JsonConstants.OpenBrace))
                    EnsureBufferConstant();
            }

            _indent &= RemoveFlagsBitMask;
            _indent++;

        }

        private void WriteStartUtf8Pretty(ReadOnlySpan<byte> nameSpanByte, byte token)
        {
            // quote {name} quote colon open-brace, hence 4
            int bytesNeeded = 4;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            bytesNeeded += nameSpanByte.Length;

            int indent = _indent & RemoveFlagsBitMask;

            // For the new line, \r\n or \n, and the space after the colon
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + 1 + indent * 2;

            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            // \r\n versus \n, depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
            {
                byteBuffer[idx++] = JsonConstants.CarriageReturn;
            }

            byteBuffer[idx++] = JsonConstants.LineFeed;

            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            nameSpanByte.CopyTo(byteBuffer.Slice(idx));

            idx += nameSpanByte.Length;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = token;

            _bufferWriter.Advance(idx);
        }

        private void WriteStartUtf16Pretty(ReadOnlySpan<byte> nameSpanByte, byte token)
        {
            // quote {name} quote colon open-brace, hence 4
            int bytesNeeded = 4;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            if (Encodings.Utf16.ToUtf8Length(nameSpanByte, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededValue;

            int indent = _indent & RemoveFlagsBitMask;

            // For the new line, \r\n or \n, and the space after the colon
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + 1 + indent * 2;

            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            // \r\n versus \n, depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
            {
                byteBuffer[idx++] = JsonConstants.CarriageReturn;
            }

            byteBuffer[idx++] = JsonConstants.LineFeed;

            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(consumed == nameSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = token;

            _bufferWriter.Advance(idx);
        }

        private void WriteStartUtf8Pretty(ReadOnlySpan<byte> nameSpanByte, int bytesNeeded, byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            // \r\n versus \n, depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
            {
                byteBuffer[idx++] = JsonConstants.CarriageReturn;
            }

            byteBuffer[idx++] = JsonConstants.LineFeed;

            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(consumed == nameSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = token;

            _bufferWriter.Advance(idx);
        }

        private bool TryWriteStartUtf8(ReadOnlySpan<byte> nameSpanByte, byte token)
        {
            int idx = 0;

            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;
                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                bool status = nameSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;

                idx += nameSpanByte.Length;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                byteBuffer[idx++] = token;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            return true;
        }

        private bool TryWriteStartUtf16(ReadOnlySpan<byte> nameSpanByte, byte token)
        {
            int idx = 0;

            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;
                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
                if (status == OperationStatus.DestinationTooSmall) return false;
                if (status != OperationStatus.Done)
                {
                    JsonThrowHelper.ThrowFormatException();
                }
                Debug.Assert(consumed == nameSpanByte.Length);
                idx += written;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                byteBuffer[idx++] = token;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            return true;
        }

        private void WriteStartUtf8(ReadOnlySpan<byte> nameSpanByte, int bytesNeeded, byte token)
        {
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(consumed == nameSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = token;

            _bufferWriter.Advance(idx);
        }

        /// <summary>
        /// Writes the end tag for an object.
        /// </summary>
        public void WriteObjectEnd()
        {
            _indent |= 1 << 31;
            _indent--;

            if (_prettyPrint)
            {
                WriteEndUtf8Pretty(JsonConstants.CloseBrace);
            }
            else
            {
                _bufferWriter.Ensure(1);
                Span<byte> byteBuffer = _bufferWriter.Buffer;
                byteBuffer[0] = JsonConstants.CloseBrace;
                _bufferWriter.Advance(1);
            }
        }

        private void WriteEndUtf8Pretty(byte token)
        {
            int indent = _indent & RemoveFlagsBitMask;

            // For the new line, \r\n or \n + indentation + }
            int bytesNeeded = 1 + JsonWriterHelper.NewLineUtf8.Length + indent * 2;

            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            // \r\n versus \n, depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
            {
                byteBuffer[idx++] = JsonConstants.CarriageReturn;
            }

            byteBuffer[idx++] = JsonConstants.LineFeed;

            while (indent-- > 0)
            {
                byteBuffer[idx++] = JsonConstants.Space;
                byteBuffer[idx++] = JsonConstants.Space;
            }

            byteBuffer[idx] = token;
            _bufferWriter.Advance(bytesNeeded);
        }

        /// <summary>
        /// Write the starting tag of an array. This is used for adding an array to a nested
        /// array of other items. If this is used while inside a nested object, the property
        /// name will be missing and result in invalid JSON.
        /// </summary>
        public void WriteArrayStart()
        {
            WriteStartUtf8(CalculateStartBytesNeeded(sizeof(byte)), JsonConstants.OpenBracket);

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        /// <summary>
        /// Write the starting tag of an array. This is used for adding an array to a
        /// nested object. If this is used while inside a nested array, the property
        /// name will be written and result in invalid JSON.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        public void WriteArrayStart(string name)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());

            if (_prettyPrint)
            {
                WriteStartUtf16Pretty(nameSpan, JsonConstants.OpenBracket);
            }
            else
            {
                while (!TryWriteStartUtf16(nameSpan, JsonConstants.OpenBracket))
                    EnsureBufferConstant();
            }

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        public void WriteArrayStart(ReadOnlySpan<byte> name)
        {
            if (_prettyPrint)
            {
                WriteStartUtf8Pretty(name, JsonConstants.OpenBracket);
            }
            else
            {
                while (!TryWriteStartUtf8(name, JsonConstants.OpenBracket))
                    EnsureBufferConstant();
            }

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        public void WriteObjectStart(ReadOnlySpan<byte> name)
        {
            if (_prettyPrint)
            {
                WriteStartUtf8Pretty(name, JsonConstants.OpenBrace);
            }
            else
            {
                while (!TryWriteStartUtf8(name, JsonConstants.OpenBrace))
                    EnsureBufferConstant();
            }

            _indent &= RemoveFlagsBitMask;
            _indent++;
        }

        //TODO: Implement UTF-8 specific overloads

        public void WriteAttribute(ReadOnlySpan<byte> name, bool value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, DateTime value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, DateTimeOffset value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, Guid value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, long value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, string value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttributeUtf8(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
            if (_prettyPrint)
            {
                //TODO: Add implementation that doesn't encode.
                WriteAttributeUtf8Pretty(name, value);
            }
            else
            {
                while (!TryWriteAttributeAlreadyUtf8(name, value))
                    EnsureBufferConstant();
            }
        }

        public void WriteAttributeValueUtf8(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
        {
            if (_prettyPrint)
            {
                //TODO
            }
            else
            {
                while (!TryWriteAttributeValueAlreadyUtf8(name, value))
                    EnsureBufferConstant();
            }
        }

        private bool TryWriteAttributeValueAlreadyUtf8(ReadOnlySpan<byte> nameSpanByte, ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                bool status = nameSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;

                idx += nameSpanByte.Length;

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                status = valueSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;
                idx += valueSpanByte.Length;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private bool TryWriteValueStringAlreadyUtf8(ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                    byteBuffer[idx++] = JsonConstants.ListSeperator;

                byteBuffer[idx++] = JsonConstants.Quote;

                bool status = valueSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;
                idx += valueSpanByte.Length;
                byteBuffer[idx++] = JsonConstants.Quote;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private bool TryWriteValueAlreadyUtf8(ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                    byteBuffer[idx++] = JsonConstants.ListSeperator;

                bool status = valueSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;
                idx += valueSpanByte.Length;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private bool TryWriteAttributeAlreadyUtf8(ReadOnlySpan<byte> nameSpanByte, ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                bool status = nameSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;
                idx += nameSpanByte.Length;

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                byteBuffer[idx++] = JsonConstants.Quote;

                status = valueSpanByte.TryCopyTo(byteBuffer.Slice(idx));
                if (!status) return false;
                idx += valueSpanByte.Length;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        public void WriteAttribute(ReadOnlySpan<byte> name, ulong value)
        {
            throw new NotImplementedException();
        }

        public void WriteAttributeNull(ReadOnlySpan<byte> name)
        {
            throw new NotImplementedException();
        }

        public void WriteValue(ReadOnlySpan<byte> value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes the end tag for an array.
        /// </summary>
        public void WriteArrayEnd()
        {
            _indent |= 1 << 31;
            _indent--;

            if (_prettyPrint)
            {
                WriteEndUtf8Pretty(JsonConstants.CloseBracket);
            }
            else
            {
                _bufferWriter.Ensure(1);
                Span<byte> byteBuffer = _bufferWriter.Buffer;
                byteBuffer[0] = JsonConstants.CloseBracket;
                _bufferWriter.Advance(1);
            }
        }

        /// <summary>
        /// Write a quoted string value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The string value that will be quoted within the JSON data.</param>
        public void WriteAttribute(string name, string value)
        {
            if (value == null)
            {
                WriteAttributeNull(name);
            }
            else
            {
                ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
                ReadOnlySpan<byte> valueSpan = MemoryMarshal.AsBytes(value.AsSpan());

                if (_prettyPrint)
                {
                    WriteAttributeUtf8Pretty(nameSpan, valueSpan);
                }
                else
                {
                    while (!TryWriteAttributeUtf8(nameSpan, valueSpan))
                        EnsureBufferConstant();
                }
            }
        }

        private void WriteAttributeUtf8Pretty(ReadOnlySpan<byte> nameSpan, ReadOnlySpan<byte> valueSpan)
        {
            //quote {name} quote colon quote {value} quote, hence 5
            int bytesNeeded = 5;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            if (Encodings.Utf16.ToUtf8Length(nameSpan, out int bytesNeededName) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            if (Encodings.Utf16.ToUtf8Length(valueSpan, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededName;
            bytesNeeded += bytesNeededValue;

            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + 1 + (_indent & RemoveFlagsBitMask) * 2;
            WriteAttributeUtf8Pretty(nameSpan, valueSpan, bytesNeeded);
        }

        private void WriteAttributeUtf8Pretty(ReadOnlySpan<byte> nameSpanByte, ReadOnlySpan<byte> valueSpanByte, int bytesNeeded)
        {
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            idx += AddNewLineAndIndentation(byteBuffer.Slice(idx));

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(consumed == nameSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Space;

            byteBuffer[idx++] = JsonConstants.Quote;

            status = Encodings.Utf16.ToUtf8(valueSpanByte, byteBuffer.Slice(idx), out consumed, out written);
            Debug.Assert(consumed == valueSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
        }

        private bool TryWriteAttributeUtf8(ReadOnlySpan<byte> nameSpanByte, ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
                if (status == OperationStatus.DestinationTooSmall) return false;
                if (status != OperationStatus.Done)
                {
                    JsonThrowHelper.ThrowFormatException();
                }
                Debug.Assert(consumed == nameSpanByte.Length);
                idx += written;

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                byteBuffer[idx++] = JsonConstants.Quote;

                status = Encodings.Utf16.ToUtf8(valueSpanByte, byteBuffer.Slice(idx), out consumed, out written);
                if (status == OperationStatus.DestinationTooSmall) return false;
                if (status != OperationStatus.Done)
                {
                    JsonThrowHelper.ThrowFormatException();
                }
                Debug.Assert(consumed == valueSpanByte.Length);
                idx += written;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        //TODO: Implement escaping logic, assume buffer is large enough
        private int EscapeString(Span<byte> buffer)
        {
            // Find characters that need escaping, add reverse solidus and shift data over

            //return the amount of characters that were escaped (i.e. # of '\' added)
            return 0;
        }

        /// <summary>
        /// Write a signed integer value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The signed integer value to be written to JSON data.</param>
        public void WriteAttribute(string name, long value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            if (_prettyPrint)
            {
                WriteAttributeUtf8Pretty(nameSpan, value);
            }
            else
            {
                while (!TryWriteAttributeUtf8(nameSpan, value))
                    EnsureBufferConstant();
            }
        }

        private bool TryWriteAttributeUtf8(ReadOnlySpan<byte> nameSpanByte, long value)
        {
            int idx = 0;
            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
                if (status == OperationStatus.DestinationTooSmall) return false;
                if (status != OperationStatus.Done)
                {
                    JsonThrowHelper.ThrowFormatException();
                }
                Debug.Assert(consumed == nameSpanByte.Length);
                idx += written;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;

                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

                if (!JsonWriterHelper.TryFormatInt64Default(value, byteBuffer.Slice(idx), out int bytesWritten)) return false;
                // Using Utf8Formatter with default StandardFormat is roughly 30% slower (17 ns versus 12 ns)
                // See: https://github.com/dotnet/corefx/issues/25425
                //if (!Utf8Formatter.TryFormat(value, byteBuffer.Slice(idx), out int bytesWritten)) return false;
                idx += bytesWritten;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private void WriteAttributeUtf8Pretty(ReadOnlySpan<byte> nameSpan, long value)
        {
            // quote {name} quote colon, hence 3
            int bytesNeeded = 3;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            if (Encodings.Utf16.ToUtf8Length(nameSpan, out int bytesNeededName) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededName;

            // For the new line, \r\n or \n, and the space after the colon
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + 1 + (_indent & RemoveFlagsBitMask) * 2;

            bool insertNegationSign = false;
            if (value < 0)
            {
                insertNegationSign = true;
                value = -value;
                bytesNeeded += 1;
            }

            int digitCount = JsonWriterHelper.CountDigits((ulong)value);
            bytesNeeded += digitCount;
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);

            int idx = 0;
            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            idx += AddNewLineAndIndentation(byteBuffer.Slice(idx));

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpan, byteBuffer.Slice(idx), out int consumed, out int written);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }
            Debug.Assert(consumed == nameSpan.Length);
            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            byteBuffer[idx++] = JsonConstants.Space;

            _indent |= 1 << 31;

            if (insertNegationSign)
            {
                byteBuffer[idx++] = (byte)'-';
            }

            JsonWriterHelper.WriteDigitsUInt64D((ulong)value, byteBuffer.Slice(idx, digitCount));

            _bufferWriter.Advance(bytesNeeded);
        }

        /// <summary>
        /// Write an unsigned integer value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The unsigned integer value to be written to JSON data.</param>
        public void WriteAttribute(string name, ulong value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            WriteNumber(value); //TODO: attempt to optimize by combining this with WriteAttributeUtf8
        }

        /// <summary>
        /// Write a boolean value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The boolean value to be written to JSON data.</param>
        public void WriteAttribute(string name, bool value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            if (value)
            {
                WriteJsonValueUtf8(JsonConstants.TrueValue);
            }
            else
            {
                WriteJsonValueUtf8(JsonConstants.FalseValue);
            }
        }

        /// <summary>
        /// Write a <see cref="DateTime"/> value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The <see cref="DateTime"/> value to be written to JSON data.</param>
        public void WriteAttribute(string name, DateTime value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            WriteDateTime(value);
        }

        /// <summary>
        /// Write a <see cref="DateTimeOffset"/> value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The <see cref="DateTimeOffset"/> value to be written to JSON data.</param>
        public void WriteAttribute(string name, DateTimeOffset value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            WriteDateTimeOffset(value);
        }

        /// <summary>
        /// Write a <see cref="Guid"/> value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        /// <param name="value">The <see cref="Guid"/> value to be written to JSON data.</param>
        public void WriteAttribute(string name, Guid value)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            WriteGuid(value);
        }

        /// <summary>
        /// Write a null value along with a property name into the current object.
        /// </summary>
        /// <param name="name">The name of the property (i.e. key) within the containing object.</param>
        public void WriteAttributeNull(string name)
        {
            ReadOnlySpan<byte> nameSpan = MemoryMarshal.AsBytes(name.AsSpan());
            int bytesNeeded = CalculateStartAttributeBytesNeeded(nameSpan, sizeof(byte));
            WriteAttributeUtf8(nameSpan, bytesNeeded);
            WriteJsonValueUtf8(JsonConstants.NullValue);
        }

        private void WriteAttributeUtf8(ReadOnlySpan<byte> nameSpanByte, int bytesNeeded)
        {
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            if (_prettyPrint)
            {
                idx += AddNewLineAndIndentation(byteBuffer.Slice(idx));
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(nameSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            Debug.Assert(consumed == nameSpanByte.Length);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            byteBuffer[idx++] = JsonConstants.KeyValueSeperator;

            if (_prettyPrint)
            {
                byteBuffer[idx++] = JsonConstants.Space;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
        }

        /// <summary>
        /// Writes a quoted string value into the current array.
        /// </summary>
        /// <param name="value">The string value that will be quoted within the JSON data.</param>
        public void WriteValue(string value)
        {
            ReadOnlySpan<byte> valueSpan = MemoryMarshal.AsBytes(value.AsSpan());

            if (_prettyPrint)
            {
                WriteValueUtf8Pretty(valueSpan);
            }
            else
            {
                while (!TryWriteValueUtf8(valueSpan))
                    EnsureBufferConstant();
            }
        }

        private void WriteValueUtf8Pretty(ReadOnlySpan<byte> valueSpanByte)
        {
            int bytesNeeded = CalculateValueBytesNeededPretty(valueSpanByte);
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            idx += AddNewLineAndIndentation(byteBuffer.Slice(idx));

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(valueSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }
            Debug.Assert(consumed == valueSpanByte.Length);

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
        }

        private bool TryWriteValueUtf8(ReadOnlySpan<byte> valueSpanByte)
        {
            int idx = 0;

            try
            {
                Span<byte> byteBuffer = _bufferWriter.Buffer;

                if (_indent < 0)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                }

                byteBuffer[idx++] = JsonConstants.Quote;

                OperationStatus status = Encodings.Utf16.ToUtf8(valueSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
                if (status == OperationStatus.DestinationTooSmall) return false;
                if (status != OperationStatus.Done)
                {
                    JsonThrowHelper.ThrowFormatException();
                }
                Debug.Assert(consumed == valueSpanByte.Length);

                idx += written;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private void WriteValueUtf8(ReadOnlySpan<byte> valueSpanByte, int bytesNeeded)
        {
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;

            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            byteBuffer[idx++] = JsonConstants.Quote;

            OperationStatus status = Encodings.Utf16.ToUtf8(valueSpanByte, byteBuffer.Slice(idx), out int consumed, out int written);
            if (status != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowFormatException();
            }
            Debug.Assert(consumed == valueSpanByte.Length);

            idx += written;

            byteBuffer[idx++] = JsonConstants.Quote;

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
        }

        /// <summary>
        /// Write a signed integer value into the current array.
        /// </summary>
        /// <param name="value">The signed integer value to be written to JSON data.</param>
        public void WriteValue(long value)
        {
            if (_prettyPrint)
            {
                WriteValueUtf8Pretty(value);
            }
            else
            {
                while (!TryWriteValueUtf8(value))
                    EnsureBufferConstant();
            }
        }

        public void WriteArrayUtf8(ReadOnlySpan<byte> name, ReadOnlySpan<int> values)
        {
            if (_prettyPrint)
            {
                WriteArrayStart(name);
                for (int i = 0; i < values.Length; i++)
                    WriteValueUtf8Pretty(values[i]);
                WriteArrayEnd();
            }
            else
            {
                _bufferWriter.Ensure(values.Length * 12 + 5 + name.Length);
                Span<byte> byteBuffer = _bufferWriter.Buffer;
                int idx = 0;

                if (_indent < 0)
                    byteBuffer[idx++] = JsonConstants.ListSeperator;

                byteBuffer[idx++] = JsonConstants.Quote;
                name.CopyTo(byteBuffer.Slice(idx));
                idx += name.Length;

                if (JsonWriterHelper.IndexOfAnyEscape(byteBuffer.Slice(0, idx)) != -1)
                    idx += EscapeString(byteBuffer);

                byteBuffer[idx++] = JsonConstants.Quote;
                byteBuffer[idx++] = JsonConstants.KeyValueSeperator;
                byteBuffer[idx++] = JsonConstants.OpenBracket;

                if (values.Length != 0)
                {
                    JsonWriterHelper.TryFormatInt64Default(values[0], byteBuffer.Slice(idx), out int bytesWritten);
                    idx += bytesWritten;
                }

                for (int i = 1; i < values.Length; i++)
                {
                    byteBuffer[idx++] = JsonConstants.ListSeperator;
                    JsonWriterHelper.TryFormatInt64Default(values[i], byteBuffer.Slice(idx), out int bytesWritten);
                    idx += bytesWritten;
                }

                _indent |= 1 << 31;
                byteBuffer[idx++] = JsonConstants.CloseBracket;
                _bufferWriter.Advance(idx);
            }
        }

        private void WriteValueUtf8Pretty(long value)
        {
            int bytesNeeded = 0;
            if (_indent < 0)
            {
                bytesNeeded = 1;
            }
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + (_indent & RemoveFlagsBitMask) * 2;

            bool insertNegationSign = false;
            if (value < 0)
            {
                insertNegationSign = true;
                value = -value;
                bytesNeeded += 1;
            }

            int digitCount = JsonWriterHelper.CountDigits((ulong)value);
            bytesNeeded += digitCount;
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);

            int idx = 0;
            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            _indent |= 1 << 31;

            idx += AddNewLineAndIndentation(byteBuffer.Slice(idx));

            if (insertNegationSign)
            {
                byteBuffer[idx++] = (byte)'-';
            }

            JsonWriterHelper.WriteDigitsUInt64D((ulong)value, byteBuffer.Slice(idx, digitCount));

            _bufferWriter.Advance(bytesNeeded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryWriteValueUtf8(long value)
        {
            int idx = 0;

            Span<byte> byteBuffer = _bufferWriter.Buffer;
            if (_indent < 0)
            {
                if (byteBuffer.Length == 0) return false;
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            if (!JsonWriterHelper.TryFormatInt64Default(value, byteBuffer.Slice(idx), out int bytesWritten)) return false;
            // Using Utf8Formatter with default StandardFormat is roughly 30% slower (17 ns versus 12 ns)
            // See: https://github.com/dotnet/corefx/issues/25425
            //if (!Utf8Formatter.TryFormat(value, byteBuffer.Slice(idx), out int bytesWritten)) return false;
            idx += bytesWritten;

            _bufferWriter.Advance(idx);
            _indent |= 1 << 31;
            return true;
        }

        private void WriteValueUtf8(long value, int bytesNeeded)
        {
            bool insertNegationSign = false;
            if (value < 0)
            {
                insertNegationSign = true;
                value = -value;
                bytesNeeded += 1;
            }

            int digitCount = JsonWriterHelper.CountDigits((ulong)value);
            bytesNeeded += digitCount;
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);

            int idx = 0;
            if (_indent < 0)
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            _indent |= 1 << 31;

            if (insertNegationSign)
            {
                byteBuffer[idx++] = (byte)'-';
            }

            JsonWriterHelper.WriteDigitsUInt64D((ulong)value, byteBuffer.Slice(idx, digitCount));

            _bufferWriter.Advance(bytesNeeded);
        }

        /// <summary>
        /// Write a unsigned integer value into the current array.
        /// </summary>
        /// <param name="value">The unsigned integer value to be written to JSON data.</param>
        public void WriteValue(ulong value)
        {
            //TODO: Optimize, just like WriteValue(long value)
            WriteItemSeperatorUtf8();
            _indent |= 1 << 31;
            WriteSpacingUtf8();

            WriteNumber(value);
        }

        /// <summary>
        /// Write a boolean value into the current array.
        /// </summary>
        /// <param name="value">The boolean value to be written to JSON data.</param>
        public void WriteValue(bool value)
        {
            //TODO: Optimize, just like WriteValue(long value)
            WriteItemSeperatorUtf8();
            _indent |= 1 << 31;
            WriteSpacingUtf8();

            if (value)
            {
                WriteJsonValueUtf8(JsonConstants.TrueValue);
            }
            else
            {
                WriteJsonValueUtf8(JsonConstants.FalseValue);
            }
        }

        /// <summary>
        /// Write a <see cref="DateTime"/> value into the current array.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/> value to be written to JSON data.</param>
        public void WriteValue(DateTime value)
        {
            //TODO: Optimize, just like WriteValue(long value)
            WriteItemSeperatorUtf8();
            _indent |= 1 << 31;
            WriteSpacingUtf8();

            WriteDateTime(value);
        }

        /// <summary>
        /// Write a <see cref="DateTimeOffset"/> value into the current array.
        /// </summary>
        /// <param name="value">The <see cref="DateTimeOffset"/> value to be written to JSON data.</param>
        public void WriteValue(DateTimeOffset value)
        {
            //TODO: Optimize, just like WriteValue(long value)
            WriteItemSeperatorUtf8();
            _indent |= 1 << 31;
            WriteSpacingUtf8();

            WriteDateTimeOffset(value);
        }

        /// <summary>
        /// Write a <see cref="Guid"/> value into the current array.
        /// </summary>
        /// <param name="value">The <see cref="Guid"/> value to be written to JSON data.</param>
        public void WriteValue(Guid value)
        {
            //TODO: Optimize, just like WriteValue(long value)
            WriteItemSeperatorUtf8();
            _indent |= 1 << 31;
            WriteSpacingUtf8();

            WriteGuid(value);
        }

        /// <summary>
        /// Write a null value into the current array.
        /// </summary>
        public void WriteNull()
        {
            int bytesNeeded = (_indent >= 0 ? 0 : 1) + (_prettyPrint ? 2 + _indent * 2 : 0) + JsonConstants.NullValue.Length;
            Span<byte> byteBuffer = EnsureBuffer(bytesNeeded);
            int idx = 0;
            if (_indent >= 0)
            {
                _indent |= 1 << 31;
            }
            else
            {
                byteBuffer[idx++] = JsonConstants.ListSeperator;
            }

            if (_prettyPrint)
            {
                int indent = _indent & RemoveFlagsBitMask;
                byteBuffer[idx++] = JsonConstants.CarriageReturn;
                byteBuffer[idx++] = JsonConstants.LineFeed;

                while (indent-- > 0)
                {
                    byteBuffer[idx++] = JsonConstants.Space;
                    byteBuffer[idx++] = JsonConstants.Space;
                }
            }

            Debug.Assert(byteBuffer.Slice(idx).Length >= JsonConstants.NullValue.Length);
            JsonConstants.NullValue.CopyTo(byteBuffer.Slice(idx));

            _bufferWriter.Advance(bytesNeeded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteNumberUtf8(long value)
        {
            //TODO: Optimize, this is too slow
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!CustomFormatter.TryFormat(value, buffer, out written, JsonConstants.NumberFormat, SymbolTable.InvariantUtf8))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteNumber(ulong value)
        {
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!CustomFormatter.TryFormat(value, buffer, out written, JsonConstants.NumberFormat, SymbolTable.InvariantUtf8))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteDateTime(DateTime value)
        {
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!CustomFormatter.TryFormat(value, buffer, out written, JsonConstants.DateTimeFormat, SymbolTable.InvariantUtf8))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteDateTimeOffset(DateTimeOffset value)
        {
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!CustomFormatter.TryFormat(value, buffer, out written, JsonConstants.DateTimeFormat, SymbolTable.InvariantUtf8))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteGuid(Guid value)
        {
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!CustomFormatter.TryFormat(value, buffer, out written, JsonConstants.GuidFormat, SymbolTable.InvariantUtf8))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteJsonValueUtf8(ReadOnlySpan<byte> values)
        {
            Span<byte> buffer = _bufferWriter.Buffer;
            int written;
            while (!SymbolTable.InvariantUtf8.TryEncode(values, buffer, out int consumed, out written))
            {
                buffer = EnsureBuffer();
            }

            _bufferWriter.Advance(written);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteControlUtf8(byte value)
        {
            MemoryMarshal.GetReference(EnsureBuffer(1)) = value;
            _bufferWriter.Advance(1);
        }

        // TODO: Once public methods are optimized, remove this.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteItemSeperatorUtf8()
        {
            if (_indent >= 0)
            {
                return;
            }

            WriteControlUtf8(JsonConstants.ListSeperator);
        }

        // TODO: Once public methods are optimized, remove this.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpacingUtf8(bool newline = true)
        {
            if (!_prettyPrint)
            {
                return;
            }

            int indent = _indent & RemoveFlagsBitMask;
            int bytesNeeded = newline ? 2 : 0;
            bytesNeeded += indent * 2;

            Span<byte> buffer = EnsureBuffer(bytesNeeded);
            ref byte utf8Bytes = ref MemoryMarshal.GetReference(buffer);
            int idx = 0;

            if (newline)
            {
                Unsafe.Add(ref utf8Bytes, idx++) = JsonConstants.CarriageReturn;
                Unsafe.Add(ref utf8Bytes, idx++) = JsonConstants.LineFeed;
            }

            while (indent-- >= 0)
            {
                Unsafe.Add(ref utf8Bytes, idx++) = JsonConstants.Space;
                Unsafe.Add(ref utf8Bytes, idx++) = JsonConstants.Space;
            }

            _bufferWriter.Advance(bytesNeeded);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureBufferConstant()
        {
            _bufferWriter.Ensure(_resizeBufferBy == -1 ? 4096 : _resizeBufferBy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Span<byte> EnsureBuffer(int needed = 4096)
        {
            _bufferWriter.Ensure(needed);
            Span<byte> buffer = _bufferWriter.Buffer;
            if (buffer.Length < needed)
            {
                JsonThrowHelper.ThrowOutOfMemoryException();
            }

            return buffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CalculateStartBytesNeeded(int numBytes)
        {
            int bytesNeeded = numBytes;

            if (_indent < 0)
            {
                bytesNeeded *= 2;
            }

            return bytesNeeded;
        }

        private int CalculateValueBytesNeededPretty(ReadOnlySpan<byte> span)
        {
            int bytesNeeded = 2;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            // For the new line, \r\n or \n
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + (_indent & RemoveFlagsBitMask) * 2;

            if (Encodings.Utf16.ToUtf8Length(span, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededValue;
            return bytesNeeded;
        }

        private int CalculateValueBytesNeeded(ReadOnlySpan<byte> span)
        {
            int bytesNeeded = 2;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            if (Encodings.Utf16.ToUtf8Length(span, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededValue;
            return bytesNeeded;
        }

        private int CalculateStartAttributeBytesNeededPretty(ReadOnlySpan<byte> nameSpan)
        {
            // quote {name} quote colon, hence 3
            int bytesNeeded = 3;
            if (_indent < 0)
            {
                bytesNeeded++;
            }

            // For the new line, \r\n or \n, and the space after the colon
            bytesNeeded += JsonWriterHelper.NewLineUtf8.Length + 1 + (_indent & RemoveFlagsBitMask) * 2;

            if (Encodings.Utf16.ToUtf8Length(nameSpan, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededValue;
            return bytesNeeded;
        }

        private int CalculateStartAttributeBytesNeeded(ReadOnlySpan<byte> nameSpan, int numBytes)
        {
            int bytesNeeded = 0;
            if (_indent < 0)
            {
                bytesNeeded = numBytes;
            }

            if (_prettyPrint)
            {
                int bytesNeededForPrettyPrint = JsonWriterHelper.NewLineUtf8.Length + 1;    // For the new line, \r\n or \n, and the space after the colon
                bytesNeededForPrettyPrint += _indent * 2;
                bytesNeeded += numBytes * bytesNeededForPrettyPrint;
            }

            bytesNeeded += numBytes * 3;    // quote {name} quote colon, hence 3

            if (Encodings.Utf16.ToUtf8Length(nameSpan, out int bytesNeededValue) != OperationStatus.Done)
            {
                JsonThrowHelper.ThrowArgumentExceptionInvalidUtf8String();
            }
            bytesNeeded += bytesNeededValue;
            return bytesNeeded;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AddNewLineAndIndentation(Span<byte> buffer)
        {
            int offset = 0;
            // \r\n versus \n, depending on OS
            if (JsonWriterHelper.NewLineUtf8.Length == 2)
            {
                buffer[offset++] = JsonConstants.CarriageReturn;
            }

            buffer[offset++] = JsonConstants.LineFeed;

            int indent = _indent & RemoveFlagsBitMask;

            while (indent-- > 0)
            {
                buffer[offset++] = JsonConstants.Space;
                buffer[offset++] = JsonConstants.Space;
            }
            return offset;
        }
    }
}
