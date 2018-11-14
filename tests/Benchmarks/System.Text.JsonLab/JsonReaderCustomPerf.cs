// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using BenchmarkDotNet.Attributes;

namespace System.Text.JsonLab.Benchmarks
{
    [MemoryDiagnoser]
    public class JsonReaderCustomPerf
    {
        private string _jsonString;
        private byte[] _dataUtf8;

        [Params(65, 128, 1_000)]
        public int Depth;

        [Params(true, false)]
        public bool NestedArray;

        [GlobalSetup]
        public void Setup()
        {
            var sb = new StringBuilder();

            if (NestedArray)
            {
                for (int i = 0; i < Depth; i++)
                {
                    sb.Append("[");
                    /*sb.Append("[\"" + i + "\"");
                    if (i != Depth - 1)
                    {
                        sb.Append(",");
                    }*/
                }
                for (int i = 0; i < Depth; i++)
                {
                    sb.Append("]");
                }
            }
            else
            {
                sb.Append("{");
                for (int i = 0; i < Depth; i++)
                {
                    //sb.Append("\"message" + i + "\": ").Append("{");
                    sb.Append("\"" + i + "\":").Append("{");
                }
                for (int i = 0; i < Depth; i++)
                {
                    sb.Append("}");
                }
                sb.Append("}");
            }

            _jsonString = sb.ToString();

            _dataUtf8 = Encoding.UTF8.GetBytes(_jsonString);
        }

        /*[Benchmark]
        public void CorefxJsonReaderAllow()
        {
            var state = new Json.Corefx.JsonReaderState(maxDepth: 1_024, options: new Json.Corefx.JsonReaderOptions { CommentHandling = Json.Corefx.JsonCommentHandling.Allow });
            var json = new Json.Corefx.Utf8JsonReader(_dataUtf8, true, state);
            while (json.Read()) ;
        }

        [Benchmark]
        public void CorefxJsonReaderSkip()
        {
            var state = new Json.Corefx.JsonReaderState(maxDepth: 1_024, options: new Json.Corefx.JsonReaderOptions { CommentHandling = Json.Corefx.JsonCommentHandling.Skip });
            var json = new Json.Corefx.Utf8JsonReader(_dataUtf8, true, state);
            while (json.Read()) ;
        }*/

        [Benchmark(Baseline = true)]
        public void CorefxJsonReaderDefault()
        {
            var state = new Json.Corefx.JsonReaderState(maxDepth: 1_024, options: new Json.Corefx.JsonReaderOptions { CommentHandling = Json.Corefx.JsonCommentHandling.Disallow });
            var json = new Json.Corefx.Utf8JsonReader(_dataUtf8, true, state);
            while (json.Read()) ;
        }

        [Benchmark]
        public void CorefxJsonReaderCustomDefault()
        {
            var state = new Json.Corefx.JsonReaderStateCustom(maxDepth: 1_024, options: new Json.Corefx.JsonReaderOptions { CommentHandling = Json.Corefx.JsonCommentHandling.Disallow });
            var json = new Json.Corefx.Utf8JsonReaderCustom(_dataUtf8, true, state);
            while (json.Read()) ;
        }

        [Benchmark]
        public void CorefxJsonReaderStackDefault()
        {
            var state = new Json.Corefx.JsonReaderStateMaster(maxDepth: 1_024, options: new Json.Corefx.JsonReaderOptions { CommentHandling = Json.Corefx.JsonCommentHandling.Disallow });
            var json = new Json.Corefx.Utf8JsonReaderMaster(_dataUtf8, true, state);
            while (json.Read()) ;
        }

    }
}
