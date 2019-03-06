// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using BenchmarkDotNet.Attributes;
using Benchmarks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Utf8;

using System;
using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace System.Text.JsonLab.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 5, targetCount: 10)]
    public class JsonDeserializerPerf
    {
        private static string _jsonString =
            @"{" +
                @"""MyInt16"" : 1," +
                @"""MyInt32"" : 2," +
                @"""MyInt64"" : 3," +
                @"""MyUInt16"" : 4," +
                @"""MyUInt32"" : 5," +
                @"""MyUInt64"" : 6," +
                @"""MyByte"" : 7," +
                @"""MyChar"" : ""a""," +
                @"""MyString"" : ""Hello""," +
                @"""MyBooleanTrue"" : true," +
                @"""MyBooleanFalse"" : false," +
                @"""MySingle"" : 1.1," +
                @"""MyDouble"" : 2.2," +
                @"""MyDecimal"" : 3.3," +
                @"""MyDateTime"" : ""2019-01-30T12:01:02.0000000Z""," +
                @"""MyEnum"" : 2" + // int by default
                @"}";

        private byte[] _dataUtf8;
        private MemoryStream _memoryStream;
        private Pipe _pipe;

        [GlobalSetup]
        public void Setup()
        {
            _dataUtf8 = Encoding.UTF8.GetBytes(_jsonString);
            _memoryStream = new MemoryStream(_dataUtf8);
        }

        [Benchmark(Baseline = true)]
        public async Task StreamAsync()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            await Json.Serialization.JsonSerializer.ReadAsync<SimpleTestClass>(_memoryStream);
        }

        [Benchmark]
        public async Task PipeAsync()
        {
            _pipe = new Pipe(new PipeOptions());
            await _pipe.Writer.WriteAsync(_dataUtf8);
            _pipe.Writer.Complete();
            await Json.Serialization.JsonSerializer.ReadAsync<SimpleTestClass>(_pipe.Reader);
        }

        public enum SampleEnum
        {
            One = 1,
            Two = 2
        }

        public class SimpleTestClass
        {
            public short MyInt16 { get; set; }
            public int MyInt32 { get; set; }
            public long MyInt64 { get; set; }
            public ushort MyUInt16 { get; set; }
            public uint MyUInt32 { get; set; }
            public ulong MyUInt64 { get; set; }
            public byte MyByte { get; set; }
            public char MyChar { get; set; }
            public string MyString { get; set; }
            public decimal MyDecimal { get; set; }
            public bool MyBooleanTrue { get; set; }
            public bool MyBooleanFalse { get; set; }
            public float MySingle { get; set; }
            public double MyDouble { get; set; }
            public DateTime MyDateTime { get; set; }
            public SampleEnum MyEnum { get; set; }
        }
    }
}
