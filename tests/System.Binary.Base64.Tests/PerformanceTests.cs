// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Xunit;
using Microsoft.Xunit.Performance;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace System.Binary.Base64.Tests
{
    public class Base64PerformanceTests
    {
        private const int InnerCount = 1000;

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1000 * 1000)]
        private static void Base64Encode(int numberOfBytes)
        {
            Span<byte> source = new byte[numberOfBytes];
            Base64TestHelper.InitalizeBytes(source);
            Span<byte> destination = new byte[Base64.BytesToUtf8Length(numberOfBytes)];

            foreach (var iteration in Benchmark.Iterations) {
                using (iteration.StartMeasurement()) {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                        Base64.BytesToUtf8(source, destination, out int consumed, out int written);
                }
            }
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1000 * 1000)]
        private static void Base64EncodeBaseline(int numberOfBytes)
        {
            var source = new byte[numberOfBytes];
            Base64TestHelper.InitalizeBytes(source.AsSpan());
            var destination = new char[Base64.BytesToUtf8Length(numberOfBytes)];

            foreach (var iteration in Benchmark.Iterations) {
                using (iteration.StartMeasurement()) {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                        Convert.ToBase64CharArray(source, 0, source.Length, destination, 0);
                }
            }
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1000 * 1000)]
        private static void Base64Decode(int numberOfBytes)
        {
            Span<byte> source = new byte[numberOfBytes];
            Base64TestHelper.InitalizeBytes(source);
            Span<byte> encoded = new byte[Base64.BytesToUtf8Length(numberOfBytes)];
            Base64.BytesToUtf8(source, encoded, out int consumed, out int written);

            foreach (var iteration in Benchmark.Iterations) {
                using (iteration.StartMeasurement()) {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                        Base64.Utf8ToBytes(encoded, source, out int bytesConsumed, out int bytesWritten);
                }
            }
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1000 * 1000)]
        private static void Base64DecodeBaseline(int numberOfBytes)
        {
            var source = new byte[numberOfBytes];
            Base64TestHelper.InitalizeBytes(source.AsSpan());
            char[] encoded = Convert.ToBase64String(source).ToCharArray();

            foreach (var iteration in Benchmark.Iterations) {
                using (iteration.StartMeasurement()) {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                        Convert.FromBase64CharArray(encoded, 0, encoded.Length);
                }
            }
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(1000 * 1000)]
        private static void Base64DecodeInPlace(int numberOfBytes)
        {
            Span<byte> source = new byte[numberOfBytes];
            Base64TestHelper.InitalizeBytes(source);
            int length = Base64.BytesToUtf8Length(numberOfBytes);

            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                    {
                        Span<byte> encodedSpan = new byte[length];
                        Base64.BytesToUtf8(source, encodedSpan, out int consumed, out int written);
                        Base64.Utf8ToBytesInPlace(encodedSpan, out int bytesConsumed, out int bytesWritten);
                    }
                }
            }
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(1000)]
        [InlineData(5000)]
        [InlineData(10000)]
        [InlineData(20000)]
        [InlineData(50000)]
        private static void StichingTestNoStichingNeeded(int inputBufferSize)
        {
            Span<byte> source = new byte[inputBufferSize];
            Base64TestHelper.InitalizeDecodableBytes(source);
            Span<byte> expected = new byte[inputBufferSize];
            Base64.Utf8ToBytes(source, expected, out int expectedConsumed, out int expectedWritten);

            Base64TestHelper.SplitSourceIntoSpans(source, false, out ReadOnlySpan<byte> source1, out ReadOnlySpan<byte> source2);

            Span<byte> destination = new byte[inputBufferSize]; // Plenty of space

            int bytesConsumed = 0;
            int bytesWritten = 0;
            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                    {
                        Base64TestHelper.DecodeNoNeedToStich(source1, source2, destination, out bytesConsumed, out bytesWritten);
                    }
                }
            }

            Assert.Equal(expectedConsumed, bytesConsumed);
            Assert.Equal(expectedWritten, bytesWritten);
            Assert.True(expected.SequenceEqual(destination));
        }

        //[Benchmark(InnerIterationCount = InnerCount)]
        [InlineData(10, 1000)]
        [InlineData(32, 1000)]
        [InlineData(50, 1000)]
        [InlineData(64, 1000)]
        [InlineData(100, 1000)]
        [InlineData(500, 1000)]
        [InlineData(600, 1000)]  // No Third Call
        [InlineData(10, 5000)]
        [InlineData(32, 5000)]
        [InlineData(50, 5000)]
        [InlineData(64, 5000)]
        [InlineData(100, 5000)]
        [InlineData(500, 5000)]
        [InlineData(3000, 5000)]  // No Third Call
        [InlineData(10, 10000)]
        [InlineData(32, 10000)]
        [InlineData(50, 10000)]
        [InlineData(64, 10000)]
        [InlineData(100, 10000)]
        [InlineData(500, 10000)]
        [InlineData(6000, 10000)]  // No Third Call
        [InlineData(10, 20000)]
        [InlineData(32, 20000)]
        [InlineData(50, 20000)]
        [InlineData(64, 20000)]
        [InlineData(100, 20000)]
        [InlineData(500, 20000)]
        [InlineData(12000, 20000)]  // No Third Call
        [InlineData(10, 50000)]
        [InlineData(32, 50000)]
        [InlineData(50, 50000)]
        [InlineData(64, 50000)]
        [InlineData(100, 50000)]
        [InlineData(500, 50000)]
        [InlineData(30000, 50000)]  // No Third Call
        private static void StichingTestStichingRequired(int stackSize, int inputBufferSize)
        {
            Span<byte> source = new byte[inputBufferSize];
            Base64TestHelper.InitalizeDecodableBytes(source);
            Span<byte> expected = new byte[inputBufferSize];
            Base64.Utf8ToBytes(source, expected, out int expectedConsumed, out int expectedWritten);

            Base64TestHelper.SplitSourceIntoSpans(source, true, out ReadOnlySpan<byte> source1, out ReadOnlySpan<byte> source2);

            Span<byte> destination = new byte[inputBufferSize]; // Plenty of space
            Span<byte> stackSpan = stackalloc byte[stackSize];

            int bytesConsumed = 0;
            int bytesWritten = 0;
            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                    {
                        Base64TestHelper.DecodeStichUsingStack(source1, source2, destination, stackSpan, out bytesConsumed, out bytesWritten);
                    }
                }
            }

            Assert.Equal(expectedConsumed, bytesConsumed);
            Assert.Equal(expectedWritten, bytesWritten);
            Assert.True(expected.SequenceEqual(destination));
        }

        //[Benchmark(InnerIterationCount = 1000)]
        public void BufferReaderReadStructONE()
        {
            Assert.True(BitConverter.IsLittleEndian);

            var myStruct = new TestStruct
            {
                I0 = 12345, // 00 00 48 57 => BE => 959447040
                L1 = 4147483647,
                I2 = int.MaxValue,
                L3 = long.MinValue,
                I4 = int.MinValue,
                L5 = long.MaxValue
            };
            
            Span<byte> spanBE = new byte[Unsafe.SizeOf<TestStruct>()];

            spanBE.WriteBigEndian(myStruct.I0);
            spanBE.Slice(4).WriteBigEndian(myStruct.L1);
            spanBE.Slice(12).WriteBigEndian(myStruct.I2);
            spanBE.Slice(16).WriteBigEndian(myStruct.L3);
            spanBE.Slice(24).WriteBigEndian(myStruct.I4);
            spanBE.Slice(28).WriteBigEndian(myStruct.L5);

            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                    {
                        TestStruct readStruct = spanBE.Read<TestStruct>();

                        if (BitConverter.IsLittleEndian)
                        {
                            readStruct.I0 = readStruct.I0.ReverseEndianness();
                            readStruct.L1 = readStruct.L1.ReverseEndianness();
                            readStruct.I2 = readStruct.I2.ReverseEndianness();
                            readStruct.L3 = readStruct.L3.ReverseEndianness();
                            readStruct.I4 = readStruct.I4.ReverseEndianness();
                            readStruct.L5 = readStruct.L5.ReverseEndianness();
                        }
                    }
                }
            }

            /*Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.L1, readStruct.L1);
            Assert.Equal(myStruct.I2, readStruct.I2);
            Assert.Equal(myStruct.L3, readStruct.L3);
            Assert.Equal(myStruct.I4, readStruct.I4);
            Assert.Equal(myStruct.L5, readStruct.L5);*/
        }

        [Benchmark(InnerIterationCount = 1000)]
        public void BufferReaderReadStructTWO()
        {
            Assert.True(BitConverter.IsLittleEndian);

            var myStruct = new TestStruct
            {
                I0 = 12345, // 00 00 48 57 => BE => 959447040
                L1 = 4147483647,
                I2 = int.MaxValue,
                L3 = long.MinValue,
                I4 = int.MinValue,
                L5 = long.MaxValue
            };

            Span<byte> spanBE = new byte[Unsafe.SizeOf<TestStruct>()];

            spanBE.WriteBigEndian(myStruct.I0);
            spanBE.Slice(4).WriteBigEndian(myStruct.L1);
            spanBE.Slice(12).WriteBigEndian(myStruct.I2);
            spanBE.Slice(16).WriteBigEndian(myStruct.L3);
            spanBE.Slice(24).WriteBigEndian(myStruct.I4);
            spanBE.Slice(28).WriteBigEndian(myStruct.L5);

            foreach (var iteration in Benchmark.Iterations)
            {
                using (iteration.StartMeasurement())
                {
                    for (int i = 0; i < Benchmark.InnerIterationCount; i++)
                    {
                        TestStruct readStruct = new TestStruct
                        {
                            I0 = spanBE.ReadBigEndian<int>(),
                            L1 = spanBE.Slice(4).ReadBigEndian<long>(),
                            I2 = spanBE.Slice(12).ReadBigEndian<int>(),
                            L3 = spanBE.Slice(16).ReadBigEndian<long>(),
                            I4 = spanBE.Slice(24).ReadBigEndian<int>(),
                            L5 = spanBE.Slice(28).ReadBigEndian<long>()
                        };
                    }
                }
            }

            /*Assert.Equal(myStruct.I0, readStruct.I0);
            Assert.Equal(myStruct.L1, readStruct.L1);
            Assert.Equal(myStruct.I2, readStruct.I2);
            Assert.Equal(myStruct.L3, readStruct.L3);
            Assert.Equal(myStruct.I4, readStruct.I4);
            Assert.Equal(myStruct.L5, readStruct.L5);*/
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TestStruct
        {
            public int I0;
            public long L1;
            public int I2;
            public long L3;
            public int I4;
            public long L5;
        }
    }
}
