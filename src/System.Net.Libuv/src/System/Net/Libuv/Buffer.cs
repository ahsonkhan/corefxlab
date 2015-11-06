﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace System.Net.Libuv
{
    internal class UVBuffer : IDisposable
    {
        static NativeBufferPool _pool = new NativeBufferPool(1024, 10);
        public readonly static UVBuffer Default = new UVBuffer();

        public static UVInterop.alloc_callback_unix AllocateUnixBuffer { get; set; }
        public static UVInterop.alloc_callback_win AllocWindowsBuffer { get; set; }

        private UVBuffer() { }

        public void Dispose()
        {
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pool.Dispose();
            }
        }

        static UVBuffer()
        {
            AllocateUnixBuffer = OnAllocateUnixBuffer;
            AllocWindowsBuffer = OnAllocateWindowsBuffer;
        }

        internal static void FreeBuffer(Span<byte> buffer)
        {
            _pool.Return(buffer);
        }

        static void OnAllocateUnixBuffer(IntPtr memoryBuffer, uint length, out Unix buffer)
        {
            var memory = _pool.Rent();
            unsafe
            {
                buffer = new Unix((IntPtr)memory.UnsafePointer, (uint)memory.Length);
            }
        }

        static void OnAllocateWindowsBuffer(IntPtr memoryBuffer, uint length, out Windows buffer)
        {
            var memory = _pool.Rent();
            unsafe
            {
                buffer = new Windows((IntPtr)memory.UnsafePointer, (uint)memory.Length);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Windows
        {
            internal uint Length;
            internal IntPtr Buffer;

            internal Windows(IntPtr buffer, uint length)
            {
                Buffer = buffer;
                Length = length;
            }

            internal void Dispose()
            {
                unsafe
                {
                    var readSlice = new Span<byte>((byte*)Buffer, (int)Length);
                    FreeBuffer(readSlice);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Unix
        {
            internal IntPtr Buffer;
            internal IntPtr Length;

            internal Unix(IntPtr buffer, uint length)
            {
                Buffer = buffer;
                Length = (IntPtr)length;
            }

            internal void Dispose()
            {
                unsafe
                {
                    var readSlice = new Span<byte>((byte*)Buffer, (int)Length);
                    FreeBuffer(readSlice);
                }
            }
        }
    }
}
