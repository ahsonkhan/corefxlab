﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System.Buffers
{
    /// <summary>
    /// Writes endian-specific primitives into spans.
    /// </summary>
    /// <remarks>
    /// Use these helpers when you need to write specific endinaness.
    /// </remarks>
    public static partial class Binary
    {
        /// <summary>
        /// Writes a structure of type T into a slice of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write<[Primitive]T>(this Span<byte> buffer, T value)
            where T : struct
        {
            if ((uint)Unsafe.SizeOf<T>() > (uint)buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            Unsafe.WriteUnaligned<T>(ref buffer.DangerousGetPinnableReference(), value);
        }

        /// <summary>
        /// Writes a structure of type T into a slice of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWrite<[Primitive]T>(this Span<byte> buffer, T value)
            where T : struct
        {
            if (Unsafe.SizeOf<T>() > (uint)buffer.Length)
            {
                return false;
            }
            Unsafe.WriteUnaligned<T>(ref buffer.DangerousGetPinnableReference(), value);
            return true;
        }

        /// <summary>
        /// Writes a structure of type T to a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteBigEndian<[Primitive]T>(this Span<byte> buffer, T value) where T : struct
            => buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.Reverse(value) : value);

        /// <summary>
        /// Writes a structure of type T to a span of bytes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteLittleEndian<[Primitive]T>(this Span<byte> buffer, T value) where T : struct
            => buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.Reverse(value));

        #region WriteBigEndianSpan
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt16BigEndian(this Span<byte> buffer, short value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt32BigEndian(this Span<byte> buffer, int value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt64BigEndian(this Span<byte> buffer, long value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndiannessNew(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt16BigEndian(this Span<byte> buffer, ushort value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32BigEndian(this Span<byte> buffer, uint value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64BigEndian(this Span<byte> buffer, ulong value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndiannessNew(value) : value);
        }
        #endregion

        #region WriteLittleEndianSpan
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt16LittleEndian(this Span<byte> buffer, short value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt32LittleEndian(this Span<byte> buffer, int value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt64LittleEndian(this Span<byte> buffer, long value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndiannessNew(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt16LittleEndian(this Span<byte> buffer, ushort value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt32LittleEndian(this Span<byte> buffer, uint value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteUInt64LittleEndian(this Span<byte> buffer, ulong value)
        {
            buffer.Write(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndiannessNew(value));
        }
        #endregion

        #region TryWriteBigEndianSpan
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt16BigEndian(this Span<byte> buffer, short value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt32BigEndian(this Span<byte> buffer, int value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt64BigEndian(this Span<byte> buffer, long value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndiannessNew(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt16BigEndian(this Span<byte> buffer, ushort value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt32BigEndian(this Span<byte> buffer, uint value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndianness(value) : value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt64BigEndian(this Span<byte> buffer, ulong value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? UnsafeUtilities.ReverseEndiannessNew(value) : value);
        }
        #endregion

        #region TryWriteLittleEndianSpan
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt16LittleEndian(this Span<byte> buffer, short value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt32LittleEndian(this Span<byte> buffer, int value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteInt64LittleEndian(this Span<byte> buffer, long value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndiannessNew(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt16LittleEndian(this Span<byte> buffer, ushort value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt32LittleEndian(this Span<byte> buffer, uint value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndianness(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryWriteUInt64LittleEndian(this Span<byte> buffer, ulong value)
        {
            return buffer.TryWrite(BitConverter.IsLittleEndian ? value : UnsafeUtilities.ReverseEndiannessNew(value));
        }
        #endregion
    }
}
