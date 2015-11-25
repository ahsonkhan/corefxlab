﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace System.Text.Utf8
{
    public static class Utf8Encoder
    {
        #region Decoder
        // Should this be public?
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetNumberOfEncodedBytesFromFirstByte(byte first, out int numberOfBytes)
        {
            if ((first & 0b1000_0000) == 0)
            {
                numberOfBytes = 1;
                return true;
            }

            if ((first & 0b1110_0000) == 0b1100_0000)
            {
                numberOfBytes = 2;
                return true;
            }

            if ((first & 0b1111_0000) == 0b1110_0000)
            {
                numberOfBytes = 3;
                return true;
            }

            if ((first & 0b1111_1000) == 0b1111_0000)
            {
                numberOfBytes = 4;
                return true;
            }

            numberOfBytes = default(int);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetFirstByteCodePointValue(byte first, out UnicodeCodePoint codePoint, out int encodedBytes)
        {
            if (!TryGetNumberOfEncodedBytesFromFirstByte(first, out encodedBytes))
            {
                codePoint = default(UnicodeCodePoint);
                return false;
            }

            switch (encodedBytes)
            {
                case 1:
                    codePoint = (UnicodeCodePoint)(first & 0b0111_1111U);
                    return true;
                case 2:
                    codePoint = (UnicodeCodePoint)(first & 0b0001_1111U);
                    return true;
                case 3:
                    codePoint = (UnicodeCodePoint)(first & 0b0000_1111U);
                    return true;
                case 4:
                    codePoint = (UnicodeCodePoint)(first & 0b0000_0111U);
                    return true;
                default:
                    codePoint = default(UnicodeCodePoint);
                    encodedBytes = 0;
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryReadCodePointByte(byte nextByte, ref UnicodeCodePoint codePoint)
        {
            uint current = nextByte;
            if ((current & 0b1100_0000U) != 0b1000_0000U)
                return false;

            codePoint = new UnicodeCodePoint((codePoint.Value << 6) | (0b0011_1111U & current));
            return true;
        }

        public static bool TryDecodeCodePoint(Span<byte> buffer, out UnicodeCodePoint codePoint, out int encodedBytes)
        {
            if (buffer.Length == 0)
            {
                codePoint = default(UnicodeCodePoint);
                encodedBytes = default(int);
                return false;
            }

            byte first = buffer[0];
            if (!TryGetFirstByteCodePointValue(first, out codePoint, out encodedBytes))
                return false;

            if (buffer.Length < encodedBytes)
                return false;

            // TODO: Should we manually inline this for values 1-4 or will compiler do this for us?
            for (int i = 1; i < encodedBytes; i++)
            {
                if (!TryReadCodePointByte(buffer[i], ref codePoint))
                    return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryFindEncodedCodePointBytesCountGoingBackwards(Span<byte> buffer, out int encodedBytes)
        {
            encodedBytes = 1;
            Span<byte> it = buffer;
            // TODO: Should we have something like: Span<byte>.(Slice from the back)
            for (; encodedBytes <= UnicodeConstants.Utf8MaxCodeUnitsPerCodePoint; encodedBytes++, it = it.Slice(0, it.Length - 1))
            {
                if (it.Length == 0)
                {
                    encodedBytes = default(int);
                    return false;
                }

                // TODO: Should we have Span<byte>.Last?
                if (Utf8CodeUnit.IsFirstCodeUnitInEncodedCodePoint((Utf8CodeUnit)it[it.Length - 1]))
                {
                    // output: encodedBytes
                    return true;
                }
            }

            // Invalid unicode character or stream prematurely ended (which is still invalid character in that stream)
            encodedBytes = default(int);
            return false;
        }

        // TODO: Name TBD
        // TODO: optimize?
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDecodeCodePointBackwards(Span<byte> buffer, out UnicodeCodePoint codePoint, out int encodedBytes)
        {
            if (TryFindEncodedCodePointBytesCountGoingBackwards(buffer, out encodedBytes))
            {
                int realEncodedBytes;
                // TODO: Inline decoding, as the invalid surrogate check can be done faster
                bool ret = TryDecodeCodePoint(buffer.Slice(buffer.Length - encodedBytes, encodedBytes), out codePoint, out realEncodedBytes);
                if (ret && encodedBytes != realEncodedBytes)
                {
                    // invalid surrogate character
                    // we know the character length by iterating on surrogate characters from the end
                    // but the first byte of the character has also encoded length
                    // seems like the lengths don't match
                    return false;
                }
                return true;
            }

            codePoint = default(UnicodeCodePoint);
            encodedBytes = default(int);
            return false;
        }
        #endregion

        #region Encoder
        // Should this be public?
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNumberOfEncodedBytes(UnicodeCodePoint codePoint)
        {
            if (codePoint.Value <= 0x7F)
            {
                return 1;
            }

            if (codePoint.Value <= 0x7FF)
            {
                return 2;
            }

            if (codePoint.Value <= 0xFFFF)
            {
                return 3;
            }

            if (codePoint.Value <= 0x1FFFFF)
            {
                return 4;
            }

            return 0;
        }

        public static bool TryEncodeCodePoint(UnicodeCodePoint codePoint, Span<byte> buffer, out int encodedBytes)
        {
            if (!UnicodeCodePoint.IsSupportedCodePoint(codePoint))
            {
                encodedBytes = 0;
                return false;
            }

            encodedBytes = GetNumberOfEncodedBytes(codePoint);
            if (encodedBytes > buffer.Length)
            {
                encodedBytes = 0;
                return false;
            }

            switch (encodedBytes)
            {
                case 1:
                    buffer[0] = (byte)(0b0111_1111U & codePoint.Value);
                    return true;
                case 2:
                    buffer[0] = (byte)(((codePoint.Value >> 6) & 0b0001_1111U) | 0b1100_0000U);
                    buffer[1] = (byte)(((codePoint.Value >> 0) & 0b0011_1111U) | 0b1000_0000U);
                    return true;
                case 3:
                    buffer[0] = (byte)(((codePoint.Value >> 12) & 0b0000_1111U) | 0b1110_0000U);
                    buffer[1] = (byte)(((codePoint.Value >> 6) & 0b0011_1111U) | 0b1000_0000U);
                    buffer[2] = (byte)(((codePoint.Value >> 0) & 0b0011_1111U) | 0b1000_0000U);
                    return true;
                case 4:
                    buffer[0] = (byte)(((codePoint.Value >> 18) & 0b0000_0111U) | 0b1111_0000U);
                    buffer[1] = (byte)(((codePoint.Value >> 12) & 0b0011_1111U) | 0b1000_0000U);
                    buffer[2] = (byte)(((codePoint.Value >> 6) & 0b0011_1111U) | 0b1000_0000U);
                    buffer[3] = (byte)(((codePoint.Value >> 0) & 0b0011_1111U) | 0b1000_0000U);
                    return true;
                default:
                    return false;
            }
        }
        #endregion
    }
}
