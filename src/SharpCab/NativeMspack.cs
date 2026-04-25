using System.Runtime.InteropServices;

namespace SharpCab;

internal static class Native
{
    private const string LIB = "mspack";

    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mspack_create_cab_decompressor(IntPtr system);

    [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mspack_destroy_cab_decompressor(IntPtr cabd);

    // libmspack does NOT export cabd_open/cabd_extract/cabd_close symbols.
    // mspack_create_cab_decompressor() returns a pointer to struct mscab_decompressor,
    // whose first fields are function pointers. These wrappers call those function pointers.

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OpenDelegate(IntPtr self, IntPtr filename);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseDelegate(IntPtr self, IntPtr cabinet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ExtractDelegate(IntPtr self, IntPtr file, IntPtr filename);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int LastErrorDelegate(IntPtr self);

    [StructLayout(LayoutKind.Sequential)]
    private struct MscabDecompressorVTable
    {
        public IntPtr Open;
        public IntPtr Close;
        public IntPtr Search;
        public IntPtr Append;
        public IntPtr Prepend;
        public IntPtr Extract;
        public IntPtr SetParam;
        public IntPtr LastError;
    }

    internal static IntPtr CabdOpen(IntPtr cabd, IntPtr filename)
    {
        if (cabd == IntPtr.Zero)
            throw new ArgumentNullException(nameof(cabd));

        var table = Marshal.PtrToStructure<MscabDecompressorVTable>(cabd);
        if (table.Open == IntPtr.Zero)
            throw new EntryPointNotFoundException("libmspack CAB decompressor open function pointer is null.");

        return Marshal.GetDelegateForFunctionPointer<OpenDelegate>(table.Open)(cabd, filename);
    }

    internal static void CabdClose(IntPtr cabd, IntPtr cabinet)
    {
        if (cabd == IntPtr.Zero || cabinet == IntPtr.Zero)
            return;

        var table = Marshal.PtrToStructure<MscabDecompressorVTable>(cabd);
        if (table.Close == IntPtr.Zero)
            return;

        Marshal.GetDelegateForFunctionPointer<CloseDelegate>(table.Close)(cabd, cabinet);
    }

    internal static int CabdExtract(IntPtr cabd, IntPtr file, IntPtr filename)
    {
        if (cabd == IntPtr.Zero)
            throw new ArgumentNullException(nameof(cabd));

        var table = Marshal.PtrToStructure<MscabDecompressorVTable>(cabd);
        if (table.Extract == IntPtr.Zero)
            throw new EntryPointNotFoundException("libmspack CAB decompressor extract function pointer is null.");

        return Marshal.GetDelegateForFunctionPointer<ExtractDelegate>(table.Extract)(cabd, file, filename);
    }

    internal static int CabdLastError(IntPtr cabd)
    {
        if (cabd == IntPtr.Zero)
            return -1;

        var table = Marshal.PtrToStructure<MscabDecompressorVTable>(cabd);
        if (table.LastError == IntPtr.Zero)
            return -1;

        return Marshal.GetDelegateForFunctionPointer<LastErrorDelegate>(table.LastError)(cabd);
    }

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
