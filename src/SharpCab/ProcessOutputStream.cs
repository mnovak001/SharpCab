using System.Diagnostics;

namespace SharpCab;

internal sealed class ProcessOutputStream : Stream
{
    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Task<string> _stderrTask;
    private readonly string _fileName;
    private readonly IReadOnlyList<string> _arguments;
    private int _disposeStarted;

    public ProcessOutputStream(
        Process process,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        _process = process;
        _stdout = process.StandardOutput.BaseStream;
        _stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        _fileName = fileName;
        _arguments = arguments;
    }

    public override bool CanRead => _stdout.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _stdout.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        _stdout.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) =>
        _stdout.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _stdout.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        _stdout.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return ValueTask.CompletedTask;

        return new ValueTask(DisposeCoreAsync());
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        DisposeCoreAsync().GetAwaiter().GetResult();
        base.Dispose(disposing);
    }

    private async Task DisposeCoreAsync()
    {
        Exception? readDisposeError = null;

        try
        {
            await _stdout.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            readDisposeError = ex;
        }

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            var stderr = await _stderrTask.ConfigureAwait(false);

            if (_process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Process '{_fileName} {string.Join(" ", _arguments)}' failed with exit code {_process.ExitCode}.{Environment.NewLine}" +
                    $"stderr:{Environment.NewLine}{stderr}");
            }
        }
        finally
        {
            _process.Dispose();
        }

        if (readDisposeError is not null)
            throw readDisposeError;
    }
}