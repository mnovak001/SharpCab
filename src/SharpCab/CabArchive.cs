using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace SharpCab;

public sealed class CabArchive : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly bool _ownsInput;
    private GCHandle _inputHandle;
    private IntPtr _inputToken; // libmspack keeps this pointer as cabinet->filename, so it must outlive _cab
    private IntPtr _cabd;
    private IntPtr _cab;
    private CabEntryStream? _current;
    private Task? _extract;
    private bool _disposed;

    private CabArchive(Stream input, bool ownsInput)
    {
        _input = input;
        _ownsInput = ownsInput;
    }

    /// <summary>
    /// Opens a cabinet from a readable, seekable stream. Nothing is copied: libmspack reads and seeks the stream
    /// directly, so the stream must stay open and untouched until the archive is disposed.
    /// </summary>
    public static ValueTask<CabArchive> OpenAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("Stream must be readable and seekable; CAB parsing seeks within the archive.", nameof(stream));

        return ValueTask.FromResult(Open(stream, ownsInput: false));
    }

    public static ValueTask<CabArchive> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ValueTask.FromResult(Open(File.OpenRead(path), ownsInput: true));
    }

    private static CabArchive Open(Stream input, bool ownsInput)
    {
        var archive = new CabArchive(input, ownsInput);
        try
        {
            archive._inputHandle = GCHandle.Alloc(input);
            archive._inputToken = Native.Token(archive._inputHandle);

            archive._cabd = Native.mspack_create_cab_decompressor(Native.System);
            if (archive._cabd == IntPtr.Zero)
                throw new CabinetStreamException("Could not create libmspack CAB decompressor.");

            Native.LastException = null;
            archive._cab = Native.CabdOpen(archive._cabd, archive._inputToken);
            if (archive._cab == IntPtr.Zero)
            {
                throw new CabinetStreamException(
                    $"Could not open CAB archive with libmspack. libmspack error: {Native.CabdLastError(archive._cabd)}",
                    Native.LastException);
            }

            return archive;
        }
        catch
        {
            archive.Release();
            throw;
        }
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

    public ValueTask<Stream> OpenEntryStreamAsync(CabEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!ReferenceEquals(entry.Archive, this))
            throw new ArgumentException("Entry belongs to a different archive.", nameof(entry));

        return OpenEntryStreamAsync(entry.NativeFile, entry.Name, entry.Length, cancellationToken);
    }

    internal async ValueTask<Stream> OpenEntryStreamAsync(
        IntPtr nativeFile,
        string entryName,
        long length,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // One extraction at a time: it owns the input stream position and the decompressor state.
        if (_current is { Finished: false })
            throw new InvalidOperationException("Finish reading or dispose the current entry stream before opening another entry.");
        if (_extract is not null)
            await _extract.ConfigureAwait(false);

        var pipe = new Pipe();
        _extract = Task.Factory.StartNew(
            () => Extract(nativeFile, entryName, pipe.Writer),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return _current = new CabEntryStream(pipe.Reader, _extract, length);
    }

    // Runs on its own thread: libmspack pushes decompressed bytes into the pipe while the caller pulls from it.
    private void Extract(IntPtr nativeFile, string entryName, PipeWriter writer)
    {
        var handle = GCHandle.Alloc(new PipeWriterStream(writer));
        var token = Native.Token(handle);
        Exception? error = null;
        try
        {
            Native.LastException = null;
            int result = Native.CabdExtract(_cabd, nativeFile, token);
            if (result != 0)
            {
                error = new CabinetStreamException(
                    $"Could not extract CAB entry '{entryName}'. libmspack error: {result}",
                    Native.LastException);
            }
        }
        catch (Exception e)
        {
            error = e;
        }
        finally
        {
            Marshal.FreeCoTaskMem(token);
            handle.Free();
            writer.Complete(error);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CabArchive));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _current?.Dispose(); // aborts a still-running extraction
        if (_extract is not null)
            await _extract.ConfigureAwait(false);

        Release();
    }

    private void Release()
    {
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

        if (_inputToken != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(_inputToken);
            _inputToken = IntPtr.Zero;
        }

        if (_inputHandle.IsAllocated)
            _inputHandle.Free();

        if (_ownsInput)
            _input.Dispose();
    }
}
