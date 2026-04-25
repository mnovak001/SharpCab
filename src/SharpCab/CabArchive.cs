using System.Runtime.InteropServices;
using System.Text;

namespace SharpCab;

public sealed class CabArchive : IAsyncDisposable
{
    private readonly string _cabPath;
    private readonly bool _deleteCabOnDispose;
    private readonly List<string> _tempFiles = new();

    private IntPtr _cabd;
    private IntPtr _cab;
    private IntPtr _cabPathUtf8;
    private bool _disposed;
    private bool _entryOpen;

    private CabArchive(string cabPath, bool deleteCabOnDispose)
    {
        _cabPath = cabPath ?? throw new ArgumentNullException(nameof(cabPath));
        _deleteCabOnDispose = deleteCabOnDispose;
    }

    public static async ValueTask<CabArchive> OpenAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));

        var tempCab = Path.Combine(Path.GetTempPath(), "sharpcab-" + Guid.NewGuid().ToString("N") + ".cab");

        try
        {
            await using (var output = new FileStream(
                tempCab,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 64,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await stream.CopyToAsync(output, 1024 * 64, cancellationToken).ConfigureAwait(false);
            }

            var archive = new CabArchive(tempCab, deleteCabOnDispose: true);
            archive.OpenNative();
            return archive;
        }
        catch
        {
            TryDelete(tempCab);
            throw;
        }
    }

    public static ValueTask<CabArchive> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        var archive = new CabArchive(path, deleteCabOnDispose: false);
        archive.OpenNative();
        return ValueTask.FromResult(archive);
    }

    public IEnumerable<CabEntry> Entries
    {
        get
        {
            ThrowIfDisposed();

            if (_cab == IntPtr.Zero)
                yield break;

            var cab = Marshal.PtrToStructure<Native.CabdCabinet>(_cab);
            var filePtr = cab.Files;

            while (filePtr != IntPtr.Zero)
            {
                var file = Marshal.PtrToStructure<Native.CabdFile>(filePtr);

                string name = Marshal.PtrToStringAnsi(file.Filename) ?? string.Empty;
                long length = unchecked((long)file.Length);

                yield return new CabEntry(this, filePtr, name, length);

                filePtr = file.Next;
            }
        }
    }

    public async ValueTask<Stream> OpenEntryStreamAsync(
        CabEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));
        if (!ReferenceEquals(entry.Archive, this))
            throw new ArgumentException("Entry belongs to a different archive.", nameof(entry));

        return await OpenEntryStreamAsync(entry.NativeFile, entry.Name, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<Stream> OpenEntryStreamAsync(
        IntPtr nativeFile,
        string entryName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_entryOpen)
            throw new InvalidOperationException("Finish reading the current entry stream before opening another entry.");

        _entryOpen = true;

        var tempOutput = Path.Combine(Path.GetTempPath(), "sharpcab-entry-" + Guid.NewGuid().ToString("N") + ".bin");
        _tempFiles.Add(tempOutput);

        IntPtr outputPathUtf8 = IntPtr.Zero;

        try
        {
            outputPathUtf8 = AllocUtf8(tempOutput);
            int result = Native.CabdExtract(_cabd, nativeFile, outputPathUtf8);

            if (result != 0)
            {
                throw new CabinetStreamException(
                    $"Could not extract CAB entry '{entryName}'. libmspack error: {result}");
            }

            var stream = new DeleteOnDisposeFileStream(
                tempOutput,
                () =>
                {
                    _entryOpen = false;
                    _tempFiles.Remove(tempOutput);
                });

            await Task.CompletedTask.ConfigureAwait(false);
            return stream;
        }
        catch
        {
            _entryOpen = false;
            _tempFiles.Remove(tempOutput);
            TryDelete(tempOutput);
            throw;
        }
        finally
        {
            if (outputPathUtf8 != IntPtr.Zero)
                Marshal.FreeHGlobal(outputPathUtf8);
        }
    }

    private void OpenNative()
    {
        _cabPathUtf8 = AllocUtf8(_cabPath);

        _cabd = Native.mspack_create_cab_decompressor(IntPtr.Zero);
        if (_cabd == IntPtr.Zero)
            throw new CabinetStreamException("Could not create libmspack CAB decompressor.");

        _cab = Native.CabdOpen(_cabd, _cabPathUtf8);
        if (_cab == IntPtr.Zero)
        {
            int error = Native.CabdLastError(_cabd);
            throw new CabinetStreamException($"Could not open CAB archive with libmspack. libmspack error: {error}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CabArchive));
    }

    private static IntPtr AllocUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;

        if (_cab != IntPtr.Zero)
        {
            Native.CabdClose(_cabd, _cab);
            _cab = IntPtr.Zero;
        }

        if (_cabd != IntPtr.Zero)
        {
            Native.mspack_destroy_cab_decompressor(_cabd);
            _cabd = IntPtr.Zero;
        }

        if (_cabPathUtf8 != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_cabPathUtf8);
            _cabPathUtf8 = IntPtr.Zero;
        }

        foreach (var temp in _tempFiles.ToArray())
            TryDelete(temp);

        _tempFiles.Clear();

        if (_deleteCabOnDispose)
            TryDelete(_cabPath);

        return ValueTask.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
