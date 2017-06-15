using System;
using System.Runtime.InteropServices;

public class Program
{
    public static void Main()
    {
        UnmanagedLifetime_Native();

        UnmanagedLifetime_Array();

        ManagedLifetime_Native();

        MamangedLifetime_Array();

        ManagedReferenceCountedBuffer();

        Console.WriteLine("Press ENTER to exit");
        Console.ReadLine();
    }

    private static void ManagedReferenceCountedBuffer()
    {
        var manager = new LifetimeManager(new byte[10]);
        var buffer = manager.SmartBuffer;
        using (var handle = buffer.Retain())
        {
            var span = buffer.Span;
            Console.WriteLine("got span from smart buffer");
            try
            {
                manager.Dispose();
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("dangling reference detected");
            }
        }

        try
        {
            var span = buffer.Span;
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Use after free; smart buffer released memory");
        }
    }

    static void ManagedLifetime_Native()
    {
        var owned = new LifetimeManager(10);
        var buffer = owned.Buffer;
        var slice = buffer.Slice(1);

        var span = buffer.Span;
        var sliceSpan = slice.Span;
        Console.WriteLine("Got native spans.");

        owned.Dispose();
        try
        {
            span = buffer.Span; // this should fail
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Use after free on native memory detected!");
        }

        try
        {
            span = slice.Span; // this should fail
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Use after free on slice of native memory detected!");
        }
    }

    static void MamangedLifetime_Array()
    {
        var manager = new LifetimeManager(new byte[10]);
        manager.ArrayDisposed += new Action<byte[]>((array) => {
            Console.WriteLine("Return to the pool. Length = {0}", array.Length);
        });

        var buffer = manager.Buffer;
        var slice = buffer.Slice(1);

        var span = buffer.Span;
        var sliceSpan = slice.Span;
        Console.WriteLine("Got array spans.");

        manager.Dispose();
        try
        {
            span = buffer.Span; // this should fail
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Use after free on array detected!");
        }

        try
        {
            span = slice.Span; // this should fail
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Use after free on slice of array detected!");
        }
    }

    static void UnmanagedLifetime_Array()
    {
        var arrayBuffer = new Buffer(new byte[256]);
        var arraySpan = arrayBuffer.Span;
        // there is no way to delete/dispose the array
    }

    static unsafe void UnmanagedLifetime_Native()
    {
        var memory = Marshal.AllocHGlobal(1);
        var unmanaged = new Buffer(memory, 1);
        var span = unmanaged.Span;

        Marshal.FreeHGlobal(memory);
        var toBad = unmanaged.Span; // use after free
    }
}
