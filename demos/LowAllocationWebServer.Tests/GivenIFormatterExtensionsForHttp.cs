﻿using System;
using System.Buffers;
using System.Net.Http.Buffered;
using System.Text;
using System.Text.Formatting;
using System.Text.Utf8;
using FluentAssertions;
using Xunit;

namespace LowAllocationWebServer.Tests
{   
    public class GivenIFormatterExtensionsForHttp
    {
        private const string HttpBody = "Body Part1";

        private const string HttpMessage =
            "HTTP/1.1 200 OK\r\nConnection : close\r\nConnection : open\r\n\r\nBody Part1\r\nBody Part1";

        private readonly byte[] _statusLineInBytes = Utf8Encoding.GetBytes("HTTP/1.1 200 OK\r\n");
        private readonly byte[] _headerInBytes = Utf8Encoding.GetBytes("Connection : close\r\n");
        private readonly byte[] _updatedHeaderInBytes = Utf8Encoding.GetBytes("Connection : open \r\n");
        private readonly byte[] _httpHeaderSectionDelineatorInBytes = Utf8Encoding.GetBytes("\r\n");
        private readonly byte[] _httpBodyInBytes = Utf8Encoding.GetBytes(HttpBody);
        private readonly byte[] _httpMessageInBytes = Utf8Encoding.GetBytes(HttpMessage);

        private BufferFormatter _formatter;
        private static readonly UTF8Encoding Utf8Encoding = new UTF8Encoding();

        public GivenIFormatterExtensionsForHttp()
        {
            _formatter = new BufferFormatter(124, FormattingData.InvariantUtf8, BufferPool.Shared);
        }

        [Fact]
        public void It_has_an_extension_method_to_write_status_line()
        {            
            _formatter.WriteHttpStatusLine(
                GetUtf8EncodedString("1.1"), 
                GetUtf8EncodedString("200"), 
                GetUtf8EncodedString("OK"));

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_statusLineInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void It_has_an_extension_method_to_write_headers()
        {
            _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_headerInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void It_has_an_extension_method_to_end_the_header_session()
        {
            _formatter.EndHttpHeaderSection();

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_httpHeaderSectionDelineatorInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void It_has_an_extension_method_to_write_the_body()
        {
            _formatter.WriteHttpBody(new Utf8String(HttpBody));

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_httpBodyInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }        

        [Fact]
        public void It_is_possible_to_update_the_value_of_a_header()
        {
            var httpHeaderBuffer = 
                _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));

            httpHeaderBuffer.UpdateValue("open");

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_updatedHeaderInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void It_is_possible_to_specify_a_number_of_reserve_bytes_when_writing_the_header_and_it_is_set_to_empty_space()
        {
            _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"), 30);

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_headerInBytes);

            var filledBytesCount = 30 - _headerInBytes.Length;
            var fillingArray = new byte[filledBytesCount];            
            for (var i = 0; i < filledBytesCount; i++)
            {
                fillingArray[i] = 32;
            }

            result.Should().ContainInOrder(_headerInBytes);
            result.Should().ContainInOrder(fillingArray);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void It_is_possible_to_use_the_reserve_to_write_a_header_value_with_more_characters_than_originally_set()
        {
            var httpHeaderBuffer = 
                _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));

            httpHeaderBuffer.UpdateValue("18446744073709551615");

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(Utf8Encoding.GetBytes("Connection : 18446744073709551615\r\n"));
        }

        [Fact]
        public void HttpHeaderBuffer_UpdateValue_throws_an_exception_if_the_new_value_is_bigger_than_the_buffer_space()
        {
            var httpHeaderBuffer = 
                _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));

            Action action = () => httpHeaderBuffer.UpdateValue("close18446744073709551615a");

            action.ShouldThrow<ArgumentException>().WithMessage("newValue");
        }

        [Fact]
        public void The_http_extension_methods_can_be_composed_to_generate_the_http_message()
        {
            _formatter.WriteHttpStatusLine(GetUtf8EncodedString("1.1"), GetUtf8EncodedString("200"), GetUtf8EncodedString("OK"));
            _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));
            var httpHeaderBuffer = _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));
            _formatter.EndHttpHeaderSection();
            _formatter.WriteHttpBody(GetUtf8EncodedString(HttpBody));
            _formatter.AppendNewLine();
            _formatter.WriteHttpBody(GetUtf8EncodedString(HttpBody));

            httpHeaderBuffer.UpdateValue("open");

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(_httpMessageInBytes);

            BufferPool.Shared.ReturnBuffer(ref result);
        }

        [Fact]
        public void WriteHttpHeader_resizes_the_buffer_taking_into_consideration_the_reserver()
        {
            _formatter = new BufferFormatter(32, FormattingData.InvariantUtf8, BufferPool.Shared);

            var httpHeaderBuffer =
                _formatter.WriteHttpHeader(GetUtf8EncodedString("Connection"), GetUtf8EncodedString("close"));

            httpHeaderBuffer.UpdateValue("18446744073709551615");

            var result = _formatter.Buffer;

            result.Should().ContainInOrder(Utf8Encoding.GetBytes("Connection : 18446744073709551615\r\n"));
        }

        private static Utf8String GetUtf8EncodedString(string value)
        {
            return new Utf8String(Utf8Encoding.GetBytes(value));
        }
    }
}
