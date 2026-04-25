namespace SharpCab;

public sealed class CabEntry
{
    internal CabArchive Archive { get; }
    internal IntPtr NativeFile { get; }

    public string Name { get; }
    public long Length { get; }

    internal CabEntry(CabArchive archive, IntPtr nativeFile, string name, long length)
    {
        Archive = archive;
        NativeFile = nativeFile;
        Name = name;
        Length = length;
    }

    public ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken = default)
        => Archive.OpenEntryStreamAsync(NativeFile, Name, cancellationToken);
}