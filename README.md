# SharpCab

SharpCab is a lightweight .NET wrapper around the `libmspack` library.

It allows you to:
- List CAB archive entries
- Read files from CAB archives as streams

No temp files: libmspack reads the input stream in place and decompresses straight into the returned stream
(bounded by a 64 KB pipe), so memory use does not depend on entry size.

```csharp
await using var cab = await CabArchive.OpenAsync(stream); // or a path
foreach (var entry in cab.Entries)
{
    await using var s = await entry.OpenStreamAsync();
    await s.CopyToAsync(destination);
}
```

Notes:
- The input stream must be readable and seekable, and stay open until the archive is disposed. SharpCab never disposes a stream you passed in.
- Only one entry stream can be open per archive at a time. Read it to the end or dispose it before opening the next.
