// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.IO;

namespace System.Text.JsonLab
{
    public class Utf8JsonStream : IDisposable
    {
        private JsonReaderState _state;
        private Stream _stream;
        private byte[] _pooledArray;
        private bool _isFinalBlock;
        private int _numberOfBytes;
        private int _consumed;
        private int _startIndex;
        private long _totalConsumed;

        const int FirstSegmentSize = 1_024; //TODO: Is this necessary?
        const int StreamSegmentSize = 4_096;

        public JsonTokenType TokenType => _state._tokenType;
        public ReadOnlySpan<byte> Value => _pooledArray.AsSpan((int)_state._valueStart, (int)_state._valueLength);
        public long Consumed => _consumed + _totalConsumed;

        public Utf8JsonStream(Stream jsonStream)
        {
            if (!jsonStream.CanRead)
                JsonThrowHelper.ThrowArgumentException("Stream must be readable.");

            _stream = jsonStream;
            _pooledArray = ArrayPool<byte>.Shared.Rent(FirstSegmentSize);
            _numberOfBytes = _stream.Read(_pooledArray, 0, FirstSegmentSize);
            _isFinalBlock = _numberOfBytes == 0;
            _consumed = 0;
            _totalConsumed = 0;
            _state = default;
            _startIndex = 0;
        }

        public bool Read()
        {
            Debug.Assert(_startIndex >= 0);
            Debug.Assert(_numberOfBytes >= 0);
            Debug.Assert(_numberOfBytes >= _startIndex);
            Debug.Assert(_pooledArray.Length >= _numberOfBytes - _startIndex);

            var reader = new Utf8JsonReader(_pooledArray.AsSpan(_startIndex, _numberOfBytes - _startIndex), _isFinalBlock, _state);
            bool result = reader.Read();
            _consumed = (int)reader.Consumed;
            _totalConsumed += _consumed;
            _state = reader.State;
            if (!result && !_isFinalBlock)
            {
                return ReadNext();
            }
            else
            {
                _startIndex += _consumed;
                return result;
            }
        }

        private bool ReadNext()
        {
            bool result = false;

            do
            {
                int leftOver = _numberOfBytes - _startIndex - _consumed;
                int amountToRead = StreamSegmentSize;

                if (leftOver > 0)
                {
                    if (_consumed == 0)
                    {
                        if (leftOver > 2_000_000_000)
                            JsonThrowHelper.ThrowArgumentException("Cannot fit left over data from the previous chunk and the next chunk of data into a 2 GB buffer.");

                        // This is guaranteed to not overflow due to the check above.
                        amountToRead += leftOver;
                    }

                    if (_pooledArray.Length - leftOver < amountToRead)
                        ResizeBuffer(amountToRead, leftOver);
                    else
                    {
                        _pooledArray.AsSpan(_numberOfBytes - leftOver, leftOver).CopyTo(_pooledArray);
                        _startIndex = 0;
                    }
                }
                else
                {
                    if (_pooledArray.Length < StreamSegmentSize)
                        ResizeBuffer(StreamSegmentSize, leftOver);
                }

                _numberOfBytes = _stream.Read(_pooledArray, leftOver, amountToRead);
                _isFinalBlock = _numberOfBytes == 0; // TODO: Can this be inferred differently based on leftOver and numberOfBytes

                // This is guaranteed not to overflow
                _numberOfBytes += leftOver;
                var reader = new Utf8JsonReader(_pooledArray.AsSpan(0, _numberOfBytes), _isFinalBlock, _state);
                result = reader.Read();
                _consumed = (int)reader.Consumed;
                _totalConsumed += _consumed;
                _state = reader.State;
            } while (!result && !_isFinalBlock);

            _startIndex = _consumed;
            return result;
        }

        private void ResizeBuffer(int amountToRead, int leftOver)
        {
            Debug.Assert(amountToRead > 0);
            Debug.Assert(leftOver >= 0);
            Debug.Assert(_pooledArray != null);

            if (amountToRead >= int.MaxValue - leftOver)
            {
                JsonThrowHelper.ThrowArgumentException("Cannot fit left over data from the previous chunk and the next chunk of data into a 2 GB buffer.");
            }

            if (leftOver == 0)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = ArrayPool<byte>.Shared.Rent(amountToRead);
            }
            else
            {
                // This is guaranteed to not overflow due to the check above.
                byte[] newArray = ArrayPool<byte>.Shared.Rent(amountToRead + leftOver);
                _pooledArray.AsSpan(_numberOfBytes - leftOver).CopyTo(newArray);
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = newArray;
            }
            _startIndex = 0;
        }

        public void Dispose()
        {
            if (_pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = null;
            }
        }
    }


    public struct LocalState
    {
        internal bool _isFinalBlock;
        internal long _totalConsumed;
        internal int _consumed;
        internal long _tokenStartIndex;
        internal int _maxDepth;
        internal JsonReaderOptions _readerOptions;
    }

    public class JsonStreamReader : IDisposable
    {
        private JsonReaderState _state;
        private LocalState _localState;
        private Stream _stream;
        private byte[] _pooledArray;
        private bool _isFinalBlock;
        private int _numberOfBytes;
        private int _consumed;
        private int _startIndex;
        private int _previousStartIndex;
        private long _totalConsumed;

        const int FirstSegmentSize = 1_024; //TODO: Is this necessary?
        const int StreamSegmentSize = 4_096;

        public JsonTokenType TokenType => _state._tokenType;
        public ReadOnlySpan<byte> Value => _pooledArray.AsSpan(_previousStartIndex + (int)_state._valueStart, (int)_state._valueLength);
        public long Consumed => _consumed + _totalConsumed;

        public JsonStreamReader(Stream jsonStream)
        {
            if (!jsonStream.CanRead)
                JsonThrowHelper.ThrowArgumentException("Stream must be readable.");

            _stream = jsonStream;
            _pooledArray = ArrayPool<byte>.Shared.Rent(FirstSegmentSize);
            _numberOfBytes = _stream.Read(_pooledArray, 0, FirstSegmentSize);
            _isFinalBlock = _numberOfBytes == 0;
            _consumed = 0;
            _totalConsumed = 0;
            _state = default;

            _localState._isFinalBlock = _isFinalBlock;
            _localState._totalConsumed = 0;
            _localState._consumed = 0;
            _localState._tokenStartIndex = 0;
            _localState._maxDepth = Utf8Json.StackFreeMaxDepth;
            _localState._readerOptions = JsonReaderOptions.Default;

            _startIndex = 0;
            _previousStartIndex = 0;
        }

        public bool Read()
        {
            Debug.Assert(_startIndex >= 0);
            Debug.Assert(_numberOfBytes >= 0);
            Debug.Assert(_numberOfBytes >= _startIndex);
            Debug.Assert(_pooledArray.Length >= _numberOfBytes - _startIndex);

            _localState._consumed = 0;
            _previousStartIndex = _startIndex;
            bool result = Utf8JsonReader.Read(ref _state, _pooledArray.AsSpan(_startIndex, _numberOfBytes - _startIndex), ref _localState);
            _consumed = _localState._consumed;
            _totalConsumed += _consumed;
            if (!result && !_isFinalBlock)
            {
                return ReadNext();
            }
            else
            {
                _startIndex += _consumed;
                return result;
            }
        }

        private bool ReadNext()
        {
            bool result = false;

            do
            {
                int leftOver = _numberOfBytes - _startIndex - _consumed;
                int amountToRead = StreamSegmentSize;

                if (leftOver > 0)
                {
                    if (_consumed == 0)
                    {
                        if (leftOver > 2_000_000_000)
                            JsonThrowHelper.ThrowArgumentException("Cannot fit left over data from the previous chunk and the next chunk of data into a 2 GB buffer.");

                        // This is guaranteed to not overflow due to the check above.
                        amountToRead += leftOver;
                    }

                    if (_pooledArray.Length - leftOver < amountToRead)
                        ResizeBuffer(amountToRead, leftOver);
                    else
                    {
                        _pooledArray.AsSpan(_numberOfBytes - leftOver, leftOver).CopyTo(_pooledArray);
                        _startIndex = 0;
                    }
                }
                else
                {
                    if (_pooledArray.Length < StreamSegmentSize)
                        ResizeBuffer(StreamSegmentSize, leftOver);
                }

                _numberOfBytes = _stream.Read(_pooledArray, leftOver, amountToRead);
                _isFinalBlock = _numberOfBytes == 0; // TODO: Can this be inferred differently based on leftOver and numberOfBytes

                // This is guaranteed not to overflow
                _numberOfBytes += leftOver;
                _localState._consumed = 0;
                _previousStartIndex = 0;
                result = Utf8JsonReader.Read(ref _state, _pooledArray.AsSpan(0, _numberOfBytes), ref _localState);
                _consumed = _localState._consumed;
                _totalConsumed += _consumed;
            } while (!result && !_isFinalBlock);

            _startIndex = _consumed;
            return result;
        }

        private void ResizeBuffer(int amountToRead, int leftOver)
        {
            Debug.Assert(amountToRead > 0);
            Debug.Assert(leftOver >= 0);
            Debug.Assert(_pooledArray != null);

            if (amountToRead >= int.MaxValue - leftOver)
            {
                JsonThrowHelper.ThrowArgumentException("Cannot fit left over data from the previous chunk and the next chunk of data into a 2 GB buffer.");
            }

            if (leftOver == 0)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = ArrayPool<byte>.Shared.Rent(amountToRead);
            }
            else
            {
                // This is guaranteed to not overflow due to the check above.
                byte[] newArray = ArrayPool<byte>.Shared.Rent(amountToRead + leftOver);
                _pooledArray.AsSpan(_numberOfBytes - leftOver).CopyTo(newArray);
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = newArray;
            }
            _startIndex = 0;
        }

        public void Dispose()
        {
            if (_pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = null;
            }
        }
    }

    public ref struct Utf8JsonReaderStream
    {
        private Utf8JsonReader _jsonReader;
        private Span<byte> _buffer;
        private Stream _stream;
        private byte[] _pooledArray;
        private bool _isFinalBlock;
        private long _consumed;

        const int FirstSegmentSize = 1_024; //TODO: Is this necessary?
        const int StreamSegmentSize = 4_096;

        public JsonTokenType TokenType => _jsonReader.TokenType;
        public ReadOnlySpan<byte> Value => _jsonReader.Value;
        public long Consumed => _consumed + _jsonReader.Consumed;

        public Utf8JsonReaderStream(Stream jsonStream)
        {
            if (!jsonStream.CanRead || !jsonStream.CanSeek)
                JsonThrowHelper.ThrowArgumentException("Stream must be readable and seekable.");

            _pooledArray = ArrayPool<byte>.Shared.Rent(FirstSegmentSize);
            int numberOfBytes = jsonStream.Read(_pooledArray, 0, FirstSegmentSize);
            _buffer = _pooledArray.AsSpan(0, numberOfBytes);
            _stream = jsonStream;

            _isFinalBlock = numberOfBytes == 0;
            _jsonReader = new Utf8JsonReader(_buffer, _isFinalBlock);
            _consumed = 0;
        }

        public bool Read()
        {
            bool result = _jsonReader.Read();
            if (!result && !_isFinalBlock)
            {
                return ReadNext();
            }
            return result;
        }

        private bool ReadNext()
        {
            bool result = false;

            do
            {
                _consumed += _jsonReader.Consumed;
                int leftOver = _buffer.Length - (int)_jsonReader.Consumed;
                int amountToRead = StreamSegmentSize;
                if (leftOver > 0)
                {
                    _stream.Position -= leftOver;

                    if (_jsonReader.Consumed == 0)
                    {
                        if (leftOver > 1_000_000_000)
                            JsonThrowHelper.ThrowArgumentException("Cannot fit left over data from the previous chunk and the next chunk of data into a 2 GB buffer.");

                        // This is guaranteed to not overflow due to the check above.
                        amountToRead += leftOver * 2;
                        ResizeBuffer(amountToRead);
                    }
                }

                if (_pooledArray.Length < StreamSegmentSize)
                    ResizeBuffer(StreamSegmentSize);

                int numberOfBytes = _stream.Read(_pooledArray, 0, amountToRead);
                _isFinalBlock = numberOfBytes == 0; // TODO: Can this be inferred differently based on leftOver and numberOfBytes

                _buffer = _pooledArray.AsSpan(0, numberOfBytes);

                _jsonReader = new Utf8JsonReader(_buffer, _isFinalBlock, _jsonReader.State);
                result = _jsonReader.Read();
            } while (!result && !_isFinalBlock);

            return result;
        }

        private void ResizeBuffer(int minimumSize)
        {
            Debug.Assert(minimumSize > 0);
            Debug.Assert(_pooledArray != null);
            ArrayPool<byte>.Shared.Return(_pooledArray);
            _pooledArray = ArrayPool<byte>.Shared.Rent(minimumSize);
        }

        public void Dispose()
        {
            if (_pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(_pooledArray);
                _pooledArray = null;
            }
        }
    }
}
