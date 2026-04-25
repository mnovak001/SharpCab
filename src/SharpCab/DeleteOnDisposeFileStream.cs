namespace SharpCab;

internal sealed class DeleteOnDisposeFileStream : FileStream
{
    private readonly string _path;
    private readonly Action _onDispose;
    private bool _disposed;

    public DeleteOnDisposeFileStream(string path, Action onDispose)
        : base(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64, FileOptions.SequentialScan | FileOptions.DeleteOnClose)
    {
        _path = path;
        _onDispose = onDispose;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            base.Dispose(disposing);
            _onDispose();

            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch
            {
                // best effort cleanup
            }
        }
        else
        {
            base.Dispose(disposing);
        }
    }
}