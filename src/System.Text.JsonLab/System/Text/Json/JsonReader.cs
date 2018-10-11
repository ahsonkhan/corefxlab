// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Collections.Generic;

namespace System.Text.JsonLab
{
    public partial class JsonReader
    {
        // We are using a ulong to represent our nested state, so we can only go 64 levels deep.
        internal const int StackFreeMaxDepth = sizeof(ulong) * 8;

        // This mask represents a tiny stack to track the state during nested transitions.
        // The first bit represents the state of the current level (1 == object, 0 == array).
        // Each subsequent bit is the parent / containing type (object or array). Since this
        // reader does a linear scan, we only need to keep a single path as we go through the data.
        private ulong _containerMask;
        internal int _depth;
        internal bool _inObject;
        internal Stack<InternalJsonTokenType> _stack;
        internal JsonTokenType _tokenType;
        internal long _lineNumber;
        internal long _position;

        private bool _allowComments;
        private int _maxDepth;

        public JsonReader()
        {
            _maxDepth = StackFreeMaxDepth;
        }

        // These properties are helpers for determining the current state of the reader
        internal bool InArray => !_inObject;

        public int MaxDepth
        {
            get
            {
                return _maxDepth;
            }
            set
            {
                if (value <= 0)
                    JsonThrowHelper.ThrowArgumentException("Max depth must be positive.");
                _maxDepth = value;
                if (_maxDepth > StackFreeMaxDepth)
                    _stack = new Stack<InternalJsonTokenType>();
            }
        }

        public bool IsDefault
            => _containerMask == default &&
            _depth == default &&
            _inObject == default &&
            _stack == null &&
            _tokenType == default &&
            //_lineNumber == default && // the default _lineNumber is 1, we want IsDefault to return true for default(JsonReader)
            _position == default &&
            //_maxDepth == StackFreeMaxDepth && // the default _maxDepth is StackFreeMaxDepth, we want IsDefault to return true for default(JsonReader)
            _allowComments == default;

        public bool AllowComments
        {
            get
            {
                return _allowComments;
            }
            set
            {
                _allowComments = value;
                if (_stack == null)
                    _stack = new Stack<InternalJsonTokenType>();
            }
        }

        public JsonToken Read(ReadOnlySpan<byte> data, bool isFinalBlock = true) => new JsonToken(this, data, isFinalBlock);

        public JsonToken Read(ReadOnlySequence<byte> data, bool isFinalBlock = true) => new JsonToken(this, data, isFinalBlock);
    }
}
