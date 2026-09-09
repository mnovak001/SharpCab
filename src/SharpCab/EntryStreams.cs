using System.Buffers;
using System.IO.Pipelines;

namespace SharpCab;

/// <summary>libmspack's output "file": each write lands in the pipe and blocks while the reader is behind.</summary>
internal sealed class PipeWriterStream(PipeWriter writer) : Stream
{
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        writer.Write(buffer);
        var flush = writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        if (flush.IsCompleted)
            throw new IOException("Entry stream was closed by its reader."); // makes libmspack abort the extraction
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Flush() { }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>The stream handed to the user: pulls from the pipe; disposing it stops the extraction.</summary>
internal sealed class CabEntryStream(PipeReader reader, Task extract, long length) : Stream
{
    private readonly Stream _reader = reader.AsStream();
    private long _position;

    /// <summary>True once the reader hit EOF or was disposed, i.e. the archive may start another extraction.</summary>
    internal bool Finished { get; private set; }

    public override int Read(Span<byte> buffer) => Track(_reader.Read(buffer));
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => Track(await _reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Track(int read)
    {
        if (read == 0) Finished = true;
        _position += read;
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        _reader.Dispose(); // completes the PipeReader; a running extraction fails its next write and returns
        Finished = true;
        extract.GetAwaiter().GetResult(); // Extract() never faults
        base.Dispose(disposing);
    }

    public override void Flush() { }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
