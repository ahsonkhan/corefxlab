using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UseAfterFree
{
    public unsafe struct Buffer
    {
        byte[] _array;
        byte* _native;
        int _index; // if buffer stores byte*, index is used to store offset from the "signature" (see OwnedBuffer)
        int _length;

        // unmanaged native buffer; be careful who you pass it to
        public Buffer(byte* native, int length, bool manageLifetime = false)
        {
            if (manageLifetime)
            {
                length -= MarkerLength;
            }
            _length = length;
            _native = native;
            _array = null;
            _index = manageLifetime ? _length : Unmanaged;
        }
        public Buffer(IntPtr native, int length, bool manageLifetime = false) : this((byte*)native.ToPointer(), length, manageLifetime) { }

        public Buffer(byte[] array, int index, int length, bool managedLifetime = false)
        {
            _array = array;
            _index = index;
            _length = length;
            _native = null;
            if (managedLifetime)
            {
                _length -= MarkerLength;
                _native = (byte*)_length;
            }
        }
        public Buffer(byte[] array) : this(array, 0, array.Length) { }

        public Buffer Slice(int index)
        {
            if (_array != null)
            {
                return new Buffer(_array, _index + index, _length - _index + MarkerLength, IsManaged);
            }
            return new Buffer(_native + index, _length - index + MarkerLength, IsManaged);
        }

        public int Length => _length;
        public Span<byte> Span
        {
            get
            {
                if (IsManaged && !IsAllocated)
                {
#if DEBUG
                    throw new ObjectDisposedException("use after free detected");
#endif
                    Environment.FailFast("Use after free error detected");
                }

                if (_array != null)
                {
                    return new Span<byte>(_array, _index, _length);
                }
                return new Span<byte>(_native, _length);
            }
        }

        #region Lifetime Management
        public static ReadOnlySpan<byte> Marker => s_marker;
        const int Unmanaged = -1; // sentinel to indicate lifetime is not managed 
        const int MarkerLength = 2;
        static readonly byte[] s_marker = new byte[MarkerLength];
        bool IsManaged => (_array == null) ? _index != Unmanaged : _native != null;

        int OffsetToMarker => _array == null ? _index : (int)_native;

        bool IsAllocated
        {
            get
            {
                Span<byte> signature;
                if (_array == null)
                    signature = new Span<byte>(_native + OffsetToMarker, MarkerLength);
                else
                    signature = new Span<byte>(_array, OffsetToMarker, MarkerLength);
                if (signature.SequenceEqual(s_marker)) return true;
                return false;
            }
        }
        static Buffer()
        {
            //s_marker.AsSpan().Fill(1);
            new Random((int)Stopwatch.GetTimestamp()).NextBytes(s_marker);
        }
        #endregion
    }

    public struct SmartBuffer
    {
        Buffer _buffer;
        LifetimeManager _manager;

        public SmartBuffer(Buffer buffer, LifetimeManager manager)
        {
            _buffer = buffer;
            _manager = manager;
        }

        public static implicit operator Buffer(SmartBuffer buffer) => buffer._buffer;

        public Span<byte> Span => _buffer.Span;

        public Handle Retain()
        {
            _manager.Retain();
            return new Handle(_manager);
        }

        public struct Handle : IDisposable
        {
            private LifetimeManager _manager;

            public Handle(LifetimeManager manager)
            {
                _manager = manager;
            }

            public void Dispose()
            {
                _manager.Release();
            }
        }
    }

    public class LifetimeManager : IDisposable
    {
        byte[] _array;
        IntPtr _native;
        int _length;
        long _refcount;

        public Buffer Buffer
        {
            get
            {
                if (_array != null)
                {
                    return new Buffer(_array, 0, _array.Length, true);
                }
                unsafe
                {
                    return new Buffer((byte*)_native.ToPointer(), _length + Buffer.Marker.Length, true);
                }
            }
        }

        public SmartBuffer SmartBuffer => new SmartBuffer(Buffer, this);

        public Action<byte[]> ArrayDisposed;

        public LifetimeManager(int length)
        {
            _native = Marshal.AllocHGlobal(length + Buffer.Marker.Length);
            _length = length;
            Buffer.Marker.CopyTo(Signature);

        }
        public LifetimeManager(byte[] array)
        {
            _array = array;
            Buffer.Marker.CopyTo(Signature);

        }

        ~LifetimeManager()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            if (Interlocked.Read(ref _refcount) > 0)
            {
                throw new InvalidOperationException("outstanding references detected");
            }
            Dispose(true);
        }

        protected void Dispose(bool disposing)
        {
            Signature.Clear();
            var array = Interlocked.Exchange(ref _array, null);

            if (array != null)
            {
                var disposed = ArrayDisposed;
                if (disposed != null) disposed(array);
                _length = 0;
                GC.SuppressFinalize(this);
                return;
            }

            var native = Interlocked.Exchange(ref _native, IntPtr.Zero);
            if (native != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_native);
                _length = 0;
                GC.SuppressFinalize(this);
            }
        }

        private Span<byte> Signature
        {
            get
            {
                if (_array != null)
                {
                    return new Span<byte>(_array, _array.Length - Buffer.Marker.Length, Buffer.Marker.Length);
                }
                unsafe
                {
                    return new Span<byte>((byte*)_native.ToPointer() + _length, Buffer.Marker.Length);
                }
            }
        }

        public void Retain()
        {
            Interlocked.Increment(ref _refcount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refcount) == 0) Dispose();
        }
    }
}
