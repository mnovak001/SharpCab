namespace SharpCab;

public enum CabCompressionMethod
{
    None = 0,
    MSZip = 1,
    Quantum = 2,
    Lzx = 3,
    Unknown = 255
}

[Flags]
public enum CabFileAttributes
{
    None = 0,
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    Archive = 0x20,
    Executable = 0x40,
    UtfName = 0x80
}