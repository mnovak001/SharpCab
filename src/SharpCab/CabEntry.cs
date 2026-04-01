namespace SharpCab;

public class CabEntry(string name, long fileSize, DateTime dateTime, CabArchive parent)
{
    public string Name { get; } = name;
    public long FileSize { get; } = fileSize;
    public DateTime DateTime { get; } = dateTime;

    public Task<Stream> OpenAsync(CancellationToken cancellationToken = default)
    {
        return parent.OpenEntryAsync(Name, cancellationToken);
    }
}