// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace System.Binary.Tests
{
    public class BufferReaderTests
    {

        [Fact]
        public void BufferReaderRead()
        {
            Assert.True(BitConverter.IsLittleEndian);

            ulong value = 0x8877665544332211; // [11 22 33 44 55 66 77 88]
            Span<byte> span;
            unsafe {
                span = new Span<byte>(&value, 8);
            }

            Assert.Equal<byte>(0x11, span.ReadBigEndian<byte>());
            Assert.Equal<byte>(0x11, span.ReadLittleEndian<byte>());
            Assert.Equal<sbyte>(0x11, span.ReadBigEndian<sbyte>());
            Assert.Equal<sbyte>(0x11, span.ReadLittleEndian<sbyte>());

            Assert.Equal<ushort>(0x1122, span.ReadBigEndian<ushort>());
            Assert.Equal<ushort>(0x2211, span.ReadLittleEndian<ushort>());
            Assert.Equal<short>(0x1122, span.ReadBigEndian<short>());
            Assert.Equal<short>(0x2211, span.ReadLittleEndian<short>());

            Assert.Equal<uint>(0x11223344, span.ReadBigEndian<uint>());
            Assert.Equal<uint>(0x44332211, span.ReadLittleEndian<uint>());
            Assert.Equal<int>(0x11223344, span.ReadBigEndian<int>());
            Assert.Equal<int>(0x44332211, span.ReadLittleEndian<int>());

            Assert.Equal<ulong>(0x1122334455667788, span.ReadBigEndian<ulong>());
            Assert.Equal<ulong>(0x8877665544332211, span.ReadLittleEndian<ulong>());
            Assert.Equal<long>(0x1122334455667788, span.ReadBigEndian<long>());
            Assert.Equal<long>(unchecked((long)0x8877665544332211), span.ReadLittleEndian<long>());

        }

        [Fact]
        public void BufferReaderReadStruct()
        {
            Assert.True(BitConverter.IsLittleEndian);

            var myStruct = new TestStruct
            {
                I0 = 12345, // 00 00 48 57 => BE => 959447040
                I1 = 5316513, // 00 81 31 161 => BE => 2703184128
                I2 = 790 // 00 00 03 22 => BE => 369295360
            };

            Span<byte> spanBE = new byte[Unsafe.SizeOf<TestStruct>()];

            spanBE.WriteBigEndian(myStruct.I0);
            spanBE.Slice(4).WriteBigEndian(myStruct.I1);
            spanBE.Slice(8).WriteBigEndian(myStruct.I2);

            TestStruct readStruct = spanBE.Read<TestStruct>();

            if (BitConverter.IsLittleEndian)
            {
                readStruct.I0 = readStruct.I0.ReverseEndianness();
                readStruct.I1 = readStruct.I1.ReverseEndianness();
                readStruct.I2 = readStruct.I2.ReverseEndianness();
            }

            Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.I1, readStruct.I1);
            Assert.Equal(myStruct.I2, readStruct.I2);

            /*byte[] array0 = BitConverter.GetBytes(12345);
            byte[] array1 = BitConverter.GetBytes(5316513);
            byte[] array2 = BitConverter.GetBytes(790);

            int length = array0.Length + array1.Length + array2.Length;

            byte[] myArray = new byte[length];
            Array.Copy(array0, myArray, array0.Length);
            Array.Copy(array1, 0, myArray, array0.Length, array1.Length);
            Array.Copy(array2, 0, myArray, array0.Length + array1.Length, array2.Length);

            Span<byte> span = myArray;

            TestStruct readStruct = span.Read<TestStruct>();

            TestStruct readStructLE = span.ReadLittleEndian<TestStruct>();
            TestStruct readStructBE = span.ReadBigEndian<TestStruct>();

            Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.I1, readStruct.I1);
            Assert.Equal(myStruct.I2, readStruct.I2);

            Assert.Equal(myStruct.I0, readStructLE.I0);
            Assert.Equal(myStruct.I1, readStructLE.I1);
            Assert.Equal(myStruct.I2, readStructLE.I2);

            Assert.Equal(((Span<byte>)array0).ReadBigEndian<int>(), readStructBE.I0);
            Assert.Equal(((Span<byte>)array1).ReadBigEndian<int>(), readStructBE.I1);
            Assert.Equal(((Span<byte>)array2).ReadBigEndian<int>(), readStructBE.I2);*/

        }

        [Fact]
        public void BufferReaderReadStructONE()
        {
            Assert.True(BitConverter.IsLittleEndian);

            var myStruct = new TestStruct2
            {
                I0 = 12345, // 00 00 48 57 => BE => 959447040
                L1 = 4147483647,
                I2 = int.MaxValue,
                L3 = long.MinValue,
                I4 = int.MinValue,
                L5 = long.MaxValue
            };

            Span<byte> spanBE = new byte[Unsafe.SizeOf<TestStruct2>()];

            spanBE.WriteBigEndian(myStruct.I0);
            spanBE.Slice(4).WriteBigEndian(myStruct.L1);
            spanBE.Slice(12).WriteBigEndian(myStruct.I2);
            spanBE.Slice(16).WriteBigEndian(myStruct.L3);
            spanBE.Slice(24).WriteBigEndian(myStruct.I4);
            spanBE.Slice(28).WriteBigEndian(myStruct.L5);

            TestStruct2 readStruct = spanBE.Read<TestStruct2>();

            if (BitConverter.IsLittleEndian)
            {
                readStruct.I0 = readStruct.I0.ReverseEndianness();
                readStruct.L1 = readStruct.L1.ReverseEndianness();
                readStruct.I2 = readStruct.I2.ReverseEndianness();
                readStruct.L3 = readStruct.L3.ReverseEndianness();
                readStruct.I4 = readStruct.I4.ReverseEndianness();
                readStruct.L5 = readStruct.L5.ReverseEndianness();
            }

            Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.L1, readStruct.L1);
            Assert.Equal(myStruct.I2, readStruct.I2);
            Assert.Equal(myStruct.L3, readStruct.L3);
            Assert.Equal(myStruct.I4, readStruct.I4);
            Assert.Equal(myStruct.L5, readStruct.L5);
        }

        [Fact]
        public void BufferReaderReadStructTWO()
        {
            Assert.True(BitConverter.IsLittleEndian);

            var myStruct = new TestStruct2
            {
                I0 = 12345, // 00 00 48 57 => BE => 959447040
                L1 = 4147483647,
                I2 = int.MaxValue,
                L3 = long.MinValue,
                I4 = int.MinValue,
                L5 = long.MaxValue
            };

            Span<byte> spanBE = new byte[Unsafe.SizeOf<TestStruct2>()];

            spanBE.WriteBigEndian(myStruct.I0);
            spanBE.Slice(4).WriteBigEndian(myStruct.L1);
            spanBE.Slice(12).WriteBigEndian(myStruct.I2);
            spanBE.Slice(16).WriteBigEndian(myStruct.L3);
            spanBE.Slice(24).WriteBigEndian(myStruct.I4);
            spanBE.Slice(28).WriteBigEndian(myStruct.L5);

            TestStruct2 readStruct = new TestStruct2
            {
                I0 = spanBE.ReadBigEndian<int>(),
                L1 = spanBE.Slice(4).ReadBigEndian<long>(),
                I2 = spanBE.Slice(12).ReadBigEndian<int>(),
                L3 = spanBE.Slice(16).ReadBigEndian<long>(),
                I4 = spanBE.Slice(24).ReadBigEndian<int>(),
                L5 = spanBE.Slice(28).ReadBigEndian<long>()
            };
            
            Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.L1, readStruct.L1);
            Assert.Equal(myStruct.I2, readStruct.I2);
            Assert.Equal(myStruct.L3, readStruct.L3);
            Assert.Equal(myStruct.I4, readStruct.I4);
            Assert.Equal(myStruct.L5, readStruct.L5);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestStruct2
        {
            public int I0;
            public long L1;
            public int I2;
            public long L3;
            public int I4;
            public long L5;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestStruct
        {
            public int I0;
            public int I1;
            public int I2;
        }
    }
}
