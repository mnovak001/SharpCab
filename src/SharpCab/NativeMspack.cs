using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCab;

internal static unsafe class Native
{
    private const string LIB = "mspack";

    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mspack_create_cab_decompressor(IntPtr system);

    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mspack_destroy_cab_decompressor(IntPtr cabd);

    // struct mscab_decompressor is a table of function pointers; libmspack exports no cabd_* symbols.
    [StructLayout(LayoutKind.Sequential)]
    private struct Decompressor
    {
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr> Open;
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void> Close;
        public IntPtr Search, Append, Prepend;
        public delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int> Extract;
        public IntPtr SetParam;
        public delegate* unmanaged[Cdecl]<IntPtr, int> LastError;
    }

    internal static IntPtr CabdOpen(IntPtr cabd, IntPtr filename) => ((Decompressor*)cabd)->Open(cabd, filename);
    internal static void CabdClose(IntPtr cabd, IntPtr cab) => ((Decompressor*)cabd)->Close(cabd, cab);
    internal static int CabdExtract(IntPtr cabd, IntPtr file, IntPtr filename) => ((Decompressor*)cabd)->Extract(cabd, file, filename);
    internal static int CabdLastError(IntPtr cabd) => ((Decompressor*)cabd)->LastError(cabd);

    // struct mspack_system: the I/O layer libmspack calls instead of stdio. Every "filename" we hand to libmspack
    // is the GCHandle of a .NET Stream (as a decimal string), and the "file" it gets back is that same handle.
    [StructLayout(LayoutKind.Sequential)]
    private struct MspackSystem
    {
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, int, IntPtr> Open;
        public delegate* unmanaged[Cdecl]<IntPtr, void> Close;
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int> Read;
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, int, int> Write;
        public delegate* unmanaged[Cdecl]<IntPtr, long, int, int> Seek; // off_t: 64-bit on the supported RIDs
        public delegate* unmanaged[Cdecl]<IntPtr, long> Tell;
        public delegate* unmanaged[Cdecl]<IntPtr, byte*, void> Message; // variadic in C; we ignore the arguments
        public delegate* unmanaged[Cdecl]<IntPtr, nuint, void*> Alloc;
        public delegate* unmanaged[Cdecl]<void*, void> Free;
        public delegate* unmanaged[Cdecl]<void*, void*, nuint, void> Copy;
        public IntPtr NullPtr;
    }

    internal static readonly IntPtr System = CreateSystem();

    private static IntPtr CreateSystem()
    {
        var s = (MspackSystem*)NativeMemory.AllocZeroed((nuint)sizeof(MspackSystem));
        s->Open = &Open;
        s->Close = &Close;
        s->Read = &Read;
        s->Write = &Write;
        s->Seek = &Seek;
        s->Tell = &Tell;
        s->Message = &Message;
        s->Alloc = &Alloc;
        s->Free = &Free;
        s->Copy = &Copy;
        return (IntPtr)s;
    }

    /// <summary>The .NET exception behind the last failed callback on this thread, if any.</summary>
    [ThreadStatic] internal static Exception? LastException;

    internal static IntPtr Token(GCHandle streamHandle)
        => Marshal.StringToCoTaskMemUTF8(GCHandle.ToIntPtr(streamHandle).ToString());

    private static Stream Target(IntPtr file) => (Stream)GCHandle.FromIntPtr(file).Target!;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr Open(IntPtr self, byte* filename, int mode)
    {
        try { return nint.Parse(Marshal.PtrToStringUTF8((IntPtr)filename)!); }
        catch (Exception e) { LastException = e; return IntPtr.Zero; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Close(IntPtr file) { } // stream lifetime is owned by CabArchive

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Read(IntPtr file, byte* buffer, int bytes)
    {
        // libmspack treats a short read as an error, so fill the buffer unless EOF.
        try { return Target(file).ReadAtLeast(new Span<byte>(buffer, bytes), bytes, throwOnEndOfStream: false); }
        catch (Exception e) { LastException = e; return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Write(IntPtr file, byte* buffer, int bytes)
    {
        try { Target(file).Write(new ReadOnlySpan<byte>(buffer, bytes)); return bytes; }
        catch (Exception e) { LastException = e; return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int Seek(IntPtr file, long offset, int mode)
    {
        try { Target(file).Seek(offset, (SeekOrigin)mode); return 0; } // MSPACK_SYS_SEEK_* == SeekOrigin
        catch (Exception e) { LastException = e; return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long Tell(IntPtr file)
    {
        try { return Target(file).Position; }
        catch (Exception e) { LastException = e; return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Message(IntPtr file, byte* format) { }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void* Alloc(IntPtr self, nuint bytes)
    {
        try { return NativeMemory.Alloc(bytes); }
        catch (Exception e) { LastException = e; return null; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Free(void* ptr) => NativeMemory.Free(ptr);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Copy(void* src, void* dest, nuint bytes) => NativeMemory.Copy(src, dest, bytes);

    // libmspack struct mscabd_cabinet prefix through files.
    // Layout is from mspack.h. off_t is native-sized; this package assumes 64-bit Linux/macOS/Windows.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CabdCabinet
    {
        public IntPtr Next;
        public IntPtr Filename;
        public IntPtr BaseOffset;
        public uint Length;
        public IntPtr Prevcab;
        public IntPtr Nextcab;
        public IntPtr Prevname;
        public IntPtr Nextname;
        public IntPtr Previnfo;
        public IntPtr Nextinfo;
        public IntPtr Files;
        public IntPtr Folders;
    }

    // libmspack struct mscabd_file.
    [StructLayout(LayoutKind.Sequential)]
    internal struct CabdFile
    {
        public IntPtr Next;
        public IntPtr Filename;
        public uint Length;
        public int Attributes;
        public byte TimeHour;
        public byte TimeMinute;
        public byte TimeSecond;
        public byte DateDay;
        public byte DateMonth;
        public int DateYear;
        public IntPtr Folder;
        public uint Offset;
    }
}
