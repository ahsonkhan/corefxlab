// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using BenchmarkDotNet.Attributes;
using Benchmarks;
using System.Buffers;
using System.Buffers.Reader;
using System.Buffers.Tests;
using System.Collections.Generic;

namespace System.Text.JsonLab.Benchmarks
{
    // Since there are 15 tests here (5 * 3), setting low values for the warmupCount and targetCount
    //[SimpleJob(warmupCount: 3, targetCount: 5)]
    [MemoryDiagnoser]
    public class JsonReaderMultiSegmentPerf
    {
        // Keep the JsonStrings resource names in sync with TestCaseType enum values.
        public enum TestCaseType
        {
            //Json4KB,
            //Json40KB,
            Json400KB
        }

        private string _jsonString;
        private byte[] _dataUtf8;
        private Dictionary<int, ReadOnlySequence<byte>> _sequences;
        private ReadOnlySequence<byte> _sequenceSingle;

        [ParamsSource(nameof(TestCaseValues))]
        public TestCaseType TestCase;

        public static IEnumerable<TestCaseType> TestCaseValues() => (IEnumerable<TestCaseType>)Enum.GetValues(typeof(TestCaseType));

        [GlobalSetup]
        public void Setup()
        {
            _jsonString = JsonStrings.ResourceManager.GetString(TestCase.ToString());

            char[] whitespace = { ' ', '\t', '\r', '\n' };
            var builder = new StringBuilder();
            var random = new Random(42);
            for (int i = 0; i < 1_000; i++)
            {
                int index = random.Next(0, 4);
                builder.Append(whitespace[index]);
            }
            _jsonString = builder.ToString();

            _jsonString = " a";


            /*builder = new StringBuilder();
            builder.Append('\"');
            for (int i = 0; i < 1_000; i++)
            {
                builder.Append('a');
            }
            builder.Append('\"');
            _jsonString = builder.ToString();*/

            _dataUtf8 = Encoding.UTF8.GetBytes(_jsonString);

            _sequenceSingle = new ReadOnlySequence<byte>(_dataUtf8);

            int[] segmentSizes = { 1_000, 2_000, 4_000, 8_000 };

            _sequences = new Dictionary<int, ReadOnlySequence<byte>>();

            for (int i = 0; i < segmentSizes.Length; i++)
            {
                int segmentSize = segmentSizes[i];
                _sequences.Add(segmentSize, GetSequence(_dataUtf8, segmentSize));
            }
        }

        private static ReadOnlySequence<byte> GetSequence(byte[] _dataUtf8, int segmentSize)
        {
            int numberOfSegments = _dataUtf8.Length / segmentSize + 1;
            byte[][] buffers = new byte[numberOfSegments][];

            for (int j = 0; j < numberOfSegments - 1; j++)
            {
                buffers[j] = new byte[segmentSize];
                Array.Copy(_dataUtf8, j * segmentSize, buffers[j], 0, segmentSize);
            }

            int remaining = _dataUtf8.Length % segmentSize;
            buffers[numberOfSegments - 1] = new byte[remaining];
            Array.Copy(_dataUtf8, _dataUtf8.Length - remaining, buffers[numberOfSegments - 1], 0, remaining);

            return BufferFactory.Create(buffers);
        }

        //[Benchmark]
        public void SingleSegmentSequence()
        {
            var json = new Utf8JsonReader(_sequenceSingle);
            while (json.Read()) ;
        }

        //[Benchmark]
        [Arguments(4_000)]
        public void MultiSegmentSequence(int segmentSize)
        {
            var json = new Utf8JsonReader(_sequences[segmentSize]);
            while (json.Read()) ;
        }

        //[Benchmark]
        [Arguments(4_000)]
        public void MultiSegmentSequenceUsingSpan(int segmentSize)
        {
            ReadOnlySequence<byte> sequenceMultiple = _sequences[segmentSize];

            byte[] buffer = ArrayPool<byte>.Shared.Rent(segmentSize * 2);
            JsonReaderState state = default;
            int previous = 0;
            foreach (ReadOnlyMemory<byte> memory in sequenceMultiple)
            {
                ReadOnlySpan<byte> span = memory.Span;
                Span<byte> bufferSpan = buffer;
                span.CopyTo(bufferSpan.Slice(previous));
                bufferSpan = bufferSpan.Slice(0, span.Length + previous);

                bool isFinalBlock = bufferSpan.Length == 0;

                var json = new Utf8JsonReader(bufferSpan, isFinalBlock, state);
                while (json.Read()) ;

                if (isFinalBlock)
                    break;

                state = json.State;
                sequenceMultiple = sequenceMultiple.Slice(json.Consumed);

                if (json.Consumed != bufferSpan.Length)
                {
                    ReadOnlySpan<byte> leftover = sequenceMultiple.First.Span;
                    previous = leftover.Length;
                    leftover.CopyTo(buffer);
                }
                else
                {
                    previous = 0;
                }
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }

        [Benchmark]
        public void Temp1()
        {
            var reader = new BufferReader<byte>(_sequences[4_000]);
            SkipWhiteSpace(ref reader);
        }

        [Benchmark(Baseline = true)]
        public void Temp2()
        {
            var reader = _sequenceSingle.First.Span;
            SkipWhiteSpace(reader);
        }

        private int SkipWhiteSpace(ref BufferReader<byte> reader)
        {
            int _position = 0;
            while (true)
            {
                reader.TryPeek(out byte val);
                if (val != ' ' && val != '\r' && val != '\n' && val != '\t')
                {
                    break;
                }
                reader.Advance(1);
                // TODO: Does this work for Windows and Unix?
                if (val == '\n')
                {
                    _position = 0;
                }
                else
                {
                    _position++;
                }
            }
            return _position;
        }

        private int SkipWhiteSpace(ReadOnlySpan<byte> _buffer)
        {
            int _position = 0;
            //Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localCopy = _buffer;
            for (int i = 0; i < localCopy.Length; i++)
            {
                byte val = localCopy[i];
                if (val != ' ' &&
                    val != '\r' &&
                    val != '\n' &&
                    val != '\t')
                {
                    break;
                }

                if (val == '\n')
                {
                    _position = 0;
                }
                else
                {
                    _position++;
                }
            }
            return _position;
        }

        private bool ConsumeString(ref BufferReader<byte> reader)
        {
            reader.Advance(1);
            if (reader.TryReadTo(out ReadOnlySpan<byte> value, (byte)'\"', (byte)'\\', advancePastDelimiter: true))
            {
                return true;
            }
            return false;
        }

        private bool ConsumeString(ReadOnlySpan<byte> _buffer)
        {
            //Create local copy to avoid bounds checks.
            ReadOnlySpan<byte> localCopy = _buffer;

            int idx = localCopy.Slice(1).IndexOf((byte)'\"');
            if (idx < 0)
            {
                return false;
            }

            if (localCopy[idx] != '\\')
            {
                localCopy = localCopy.Slice(1, idx);
                return true;
            }
            else
            {
                return ConsumeStringWithNestedQuotes(_buffer);
            }
        }

        private bool ConsumeStringWithNestedQuotes(ReadOnlySpan<byte> _buffer)
        {
            //TODO: Optimize looking for nested quotes
            //TODO: Avoid redoing first IndexOf search
            long i = 0 + 1;
            while (true)
            {
                int counter = 0;
                int foundIdx = _buffer.Slice((int)i).IndexOf((byte)'\"');
                if (foundIdx == -1)
                {
                    return false;
                }
                if (foundIdx == 0)
                    break;
                for (long j = i + foundIdx - 1; j >= i; j--)
                {
                    if (_buffer[(int)j] != '\\')
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
            long startIndex = 1;
            ReadOnlySpan<byte> localCopy = _buffer.Slice((int)startIndex, (int)(i - startIndex));
            return true;
        }
    }
}
