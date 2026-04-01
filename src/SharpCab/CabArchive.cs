using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpCab;

public class CabArchive : IDisposable
{
    private readonly string _archivePath;
    private readonly bool _deleteFileOnDispose = false;
    private readonly ProcessRunner _processRunner = new();

    private IReadOnlyList<CabEntry>? _entries = null;

    private static readonly Regex ListLineRegex = new(
        @"^\s*(?<size>\d+)\s*\|\s*(?<date>\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2}:\d{2})\s*\|\s*(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CabArchive(string archivePath)
    {
        _archivePath = archivePath;
    }

    public CabArchive(Stream archiveStream)
    {
        _deleteFileOnDispose = true;
        var tmpFile = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write);
        archiveStream.Position = 0;
        archiveStream.CopyTo(fs);
        _archivePath = tmpFile;
    }

    public async Task<IReadOnlyList<CabEntry>> EnumerateEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (_entries is null)
        {
            var args = new[] { "-l", "-q", _archivePath };
            var result = await _processRunner.RunAsync("cabextract", args, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            result.EnsureSuccess("cabextract", args);
            _entries = ParseEntries(result.StandardOutput);
        }

        return _entries;
    }

    public async Task<Stream> OpenEntryAsync(string entryName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entryName))
            throw new ArgumentException("Entry name is required.", nameof(entryName));

        var args = new[] { "-p", "-F", entryName, _archivePath };
        var result = await _processRunner.RunForBytesAsync("cabextract", args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        result.EnsureSuccess("cabextract", args);

        return new MemoryStream(result.StandardOutputBytes, writable: false);
    }

    private IReadOnlyList<CabEntry> ParseEntries(string stdout)
    {
        var entries = new List<CabEntry>();

        using var reader = new StringReader(stdout);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var match = ListLineRegex.Match(line);
            if (!match.Success)
                continue;

            var size = long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
            var dateText = match.Groups["date"].Value;
            var name = match.Groups["name"].Value.Trim();

            var dateTime = DateTime.ParseExact(
                dateText,
                "dd.MM.yyyy HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None);

            entries.Add(new CabEntry(name, size, dateTime, this));
        }

        return entries;
    }

    public void Dispose()
    {
        if (_deleteFileOnDispose)
        {
            File.Delete(_archivePath);
        }
    }
}