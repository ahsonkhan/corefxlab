﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace System.Text.Json.Corefx.Tests
{
    internal static class JsonTestHelper
    {
        public static string NewtonsoftReturnStringHelper(TextReader reader)
        {
            var sb = new StringBuilder();
            var json = new JsonTextReader(reader);
            while (json.Read())
            {
                if (json.Value != null)
                {
                    // Use InvariantCulture to make sure numbers retain the decimal point '.'
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0}, ", json.Value);
                }
            }
            return sb.ToString();
        }

        public static string WriteDepthObject(int depth, bool writeComment = false)
        {
            var sb = new StringBuilder();
            var textWriter = new StringWriter();
            var json = new JsonTextWriter(textWriter);
            json.WriteStartObject();
            for (int i = 0; i < depth; i++)
            {
                json.WritePropertyName("message" + i);
                json.WriteStartObject();
            }
            if (writeComment)
                json.WriteComment("Random comment string");
            json.WritePropertyName("message" + depth);
            json.WriteValue("Hello, World!");
            for (int i = 0; i < depth; i++)
            {
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.Flush();

            return textWriter.ToString();
        }

        public static string WriteDepthArray(int depth, bool writeComment = false)
        {
            var sb = new StringBuilder();
            var textWriter = new StringWriter();
            var json = new JsonTextWriter(textWriter);
            json.WriteStartArray();
            for (int i = 0; i < depth; i++)
            {
                json.WriteStartArray();
            }
            if (writeComment)
                json.WriteComment("Random comment string");
            json.WriteValue("Hello, World!");
            for (int i = 0; i < depth; i++)
            {
                json.WriteEndArray();
            }
            json.WriteEndArray();
            json.Flush();

            return textWriter.ToString();
        }

        public static string WriteDepthObjectWithArray(int depth, bool writeComment = false)
        {
            var sb = new StringBuilder();
            var textWriter = new StringWriter();
            var json = new JsonTextWriter(textWriter);
            json.WriteStartObject();
            for (int i = 0; i < depth; i++)
            {
                json.WritePropertyName("message" + i);
                json.WriteStartObject();
            }
            if (writeComment)
                json.WriteComment("Random comment string");
            json.WritePropertyName("message" + depth);
            json.WriteStartArray();
            json.WriteValue("string1");
            json.WriteValue("string2");
            json.WriteEndArray();
            json.WritePropertyName("address");
            json.WriteStartObject();
            json.WritePropertyName("street");
            json.WriteValue("1 Microsoft Way");
            json.WritePropertyName("city");
            json.WriteValue("Redmond");
            json.WritePropertyName("zip");
            json.WriteValue(98052);
            json.WriteEndObject();
            for (int i = 0; i < depth; i++)
            {
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.Flush();

            return textWriter.ToString();
        }

        public static byte[] ReturnBytesHelper(byte[] data, out int length, JsonCommentHandling commentHandling = JsonCommentHandling.Disallow, int maxDepth = 64)
        {
            var state = new JsonReaderState(maxDepth: maxDepth, options: new JsonReaderOptions { CommentHandling = commentHandling });
            var reader = new Utf8JsonReader(data, true, state);
            return ReaderLoop(data.Length, out length, ref reader);
        }

        /*public static byte[] SequenceReturnBytesHelper(byte[] data, out int length, JsonCommentHandling commentHandling = JsonCommentHandling.Disallow, int maxDepth = 64)
        {
            ReadOnlySequence<byte> sequence = CreateSegments(data);
            var state = new JsonReaderState(maxDepth: maxDepth, options: new JsonReaderOptions { CommentHandling = commentHandling });
            var reader = new Utf8JsonReader(sequence, true, state);
            return ReaderLoop(data.Length, out length, ref reader);
        }*/

        public static ReadOnlySequence<byte> CreateSegments(byte[] data)
        {
            ReadOnlyMemory<byte> dataMemory = data;

            var firstSegment = new BufferSegment<byte>(dataMemory.Slice(0, data.Length / 2));
            ReadOnlyMemory<byte> secondMem = dataMemory.Slice(data.Length / 2);
            BufferSegment<byte> secondSegment = firstSegment.Append(secondMem);

            return new ReadOnlySequence<byte>(firstSegment, 0, secondSegment, secondMem.Length);
        }

        public static object ReturnObjectHelper(byte[] data, JsonCommentHandling commentHandling = JsonCommentHandling.Disallow)
        {
            var state = new JsonReaderState(options: new JsonReaderOptions { CommentHandling = commentHandling });
            var reader = new Utf8JsonReader(data, true, state);
            return ReaderLoop(ref reader);
        }

        public static string ObjectToString(object jsonValues)
        {
            string s = "";
            if (jsonValues is List<object> jsonList)
                s = ListToString(jsonList);
            else if (jsonValues is Dictionary<string, object> jsonDictionary)
                s = DictionaryToString(jsonDictionary);
            return s;
        }

        public static string DictionaryToString(Dictionary<string, object> dictionary)
        {
            var builder = new StringBuilder();
            foreach (KeyValuePair<string, object> entry in dictionary)
            {
                if (entry.Value is Dictionary<string, object> nestedDictionary)
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}, ", entry.Key).Append(DictionaryToString(nestedDictionary));
                else if (entry.Value is List<object> nestedList)
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}, ", entry.Key).Append(ListToString(nestedList));
                else
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}, {1}, ", entry.Key, entry.Value);
            }
            return builder.ToString();
        }

        public static string ListToString(List<object> list)
        {
            var builder = new StringBuilder();
            foreach (object entry in list)
            {
                if (entry is Dictionary<string, object> nestedDictionary)
                    builder.Append(DictionaryToString(nestedDictionary));
                else if (entry is List<object> nestedList)
                    builder.Append(ListToString(nestedList));
                else
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}, ", entry);
            }
            return builder.ToString();
        }

        public static byte[] ReaderLoop(int inpuDataLength, out int length, ref Utf8JsonReader json)
        {
            byte[] outputArray = new byte[inpuDataLength];
            Span<byte> destination = outputArray;

            while (json.Read())
            {
                JsonTokenType tokenType = json.TokenType;
                ReadOnlySpan<byte> valueSpan = json.ValueSpan; //json.HasValueSequence ? json.ValueSequence.ToArray() : json.ValueSpan;
                switch (tokenType)
                {
                    case JsonTokenType.PropertyName:
                        valueSpan.CopyTo(destination);
                        destination[valueSpan.Length] = (byte)',';
                        destination[valueSpan.Length + 1] = (byte)' ';
                        destination = destination.Slice(valueSpan.Length + 2);
                        break;
                    case JsonTokenType.Number:
                    case JsonTokenType.String:
                    case JsonTokenType.Comment:
                        valueSpan.CopyTo(destination);
                        destination[valueSpan.Length] = (byte)',';
                        destination[valueSpan.Length + 1] = (byte)' ';
                        destination = destination.Slice(valueSpan.Length + 2);
                        break;
                    case JsonTokenType.True:
                        // Special casing True/False so that the casing matches with Json.NET
                        destination[0] = (byte)'T';
                        destination[1] = (byte)'r';
                        destination[2] = (byte)'u';
                        destination[3] = (byte)'e';
                        destination[valueSpan.Length] = (byte)',';
                        destination[valueSpan.Length + 1] = (byte)' ';
                        destination = destination.Slice(valueSpan.Length + 2);
                        break;
                    case JsonTokenType.False:
                        destination[0] = (byte)'F';
                        destination[1] = (byte)'a';
                        destination[2] = (byte)'l';
                        destination[3] = (byte)'s';
                        destination[4] = (byte)'e';
                        destination[valueSpan.Length] = (byte)',';
                        destination[valueSpan.Length + 1] = (byte)' ';
                        destination = destination.Slice(valueSpan.Length + 2);
                        break;
                    case JsonTokenType.Null:
                        // Special casing Null so that it matches what JSON.NET does
                        break;
                    default:
                        break;
                }
            }
            length = outputArray.Length - destination.Length;
            return outputArray;
        }

        public static object ReaderLoop(ref Utf8JsonReader json)
        {
            object root = null;

            while (json.Read())
            {
                JsonTokenType tokenType = json.TokenType;
                ReadOnlySpan<byte> valueSpan = json.ValueSpan;
                switch (tokenType)
                {
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        root = valueSpan[0] == 't';
                        break;
                    case JsonTokenType.Number:
                        json.TryGetDoubleValue(out double valueDouble);
                        root = valueDouble;
                        break;
                    case JsonTokenType.String:
                        string valueString = json.GetStringValue();
                        root = valueString;
                        break;
                    case JsonTokenType.Null:
                        break;
                    case JsonTokenType.StartObject:
                        root = ReaderDictionaryLoop(ref json);
                        break;
                    case JsonTokenType.StartArray:
                        root = ReaderListLoop(ref json);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        break;
                    case JsonTokenType.None:
                    case JsonTokenType.Comment:
                    default:
                        break;
                }
            }
            return root;
        }

        public static Dictionary<string, object> ReaderDictionaryLoop(ref Utf8JsonReader json)
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object>();

            string key = "";
            object value = null;

            while (json.Read())
            {
                JsonTokenType tokenType = json.TokenType;
                ReadOnlySpan<byte> valueSpan = json.ValueSpan;
                switch (tokenType)
                {
                    case JsonTokenType.PropertyName:
                        key = json.GetStringValue();
                        dictionary.Add(key, null);
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        value = valueSpan[0] == 't';
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = value;
                        }
                        else
                        {
                            dictionary.Add(key, value);
                        }
                        break;
                    case JsonTokenType.Number:
                        json.TryGetDoubleValue(out double valueDouble);
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = valueDouble;
                        }
                        else
                        {
                            dictionary.Add(key, valueDouble);
                        }
                        break;
                    case JsonTokenType.String:
                        string valueString = json.GetStringValue();
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = valueString;
                        }
                        else
                        {
                            dictionary.Add(key, valueString);
                        }
                        break;
                    case JsonTokenType.Null:
                        value = null;
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = value;
                        }
                        else
                        {
                            dictionary.Add(key, value);
                        }
                        break;
                    case JsonTokenType.StartObject:
                        value = ReaderDictionaryLoop(ref json);
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = value;
                        }
                        else
                        {
                            dictionary.Add(key, value);
                        }
                        break;
                    case JsonTokenType.StartArray:
                        value = ReaderListLoop(ref json);
                        if (dictionary.TryGetValue(key, out _))
                        {
                            dictionary[key] = value;
                        }
                        else
                        {
                            dictionary.Add(key, value);
                        }
                        break;
                    case JsonTokenType.EndObject:
                        return dictionary;
                    case JsonTokenType.None:
                    case JsonTokenType.Comment:
                    default:
                        break;
                }
            }
            return dictionary;
        }

        public static List<object> ReaderListLoop(ref Utf8JsonReader json)
        {
            List<object> arrayList = new List<object>();

            object value = null;

            while (json.Read())
            {
                JsonTokenType tokenType = json.TokenType;
                ReadOnlySpan<byte> valueSpan = json.ValueSpan;
                switch (tokenType)
                {
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        value = valueSpan[0] == 't';
                        arrayList.Add(value);
                        break;
                    case JsonTokenType.Number:
                        json.TryGetDoubleValue(out double doubleValue);
                        arrayList.Add(doubleValue);
                        break;
                    case JsonTokenType.String:
                        string valueString = json.GetStringValue();
                        arrayList.Add(valueString);
                        break;
                    case JsonTokenType.Null:
                        value = null;
                        arrayList.Add(value);
                        break;
                    case JsonTokenType.StartObject:
                        value = ReaderDictionaryLoop(ref json);
                        arrayList.Add(value);
                        break;
                    case JsonTokenType.StartArray:
                        value = ReaderListLoop(ref json);
                        arrayList.Add(value);
                        break;
                    case JsonTokenType.EndArray:
                        return arrayList;
                    case JsonTokenType.None:
                    case JsonTokenType.Comment:
                    default:
                        break;
                }
            }
            return arrayList;
        }

        public static float NextFloat(Random random)
        {
            double mantissa = (random.NextDouble() * 2.0) - 1.0;
            double exponent = Math.Pow(2.0, random.Next(-126, 128));
            float value = (float)(mantissa * exponent);
            return value;
        }

        public static double NextDouble(Random random, double minValue, double maxValue)
        {
            double value = random.NextDouble() * (maxValue - minValue) + minValue;
            return value;
        }

        public static decimal NextDecimal(Random random, double minValue, double maxValue)
        {
            double value = random.NextDouble() * (maxValue - minValue) + minValue;
            return (decimal)value;
        }
    }
}
