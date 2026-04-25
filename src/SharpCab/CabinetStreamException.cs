namespace SharpCab;

public sealed class CabinetStreamException : Exception
{
    public CabinetStreamException(string message) : base(message) { }
    public CabinetStreamException(string message, Exception innerException) : base(message, innerException) { }
}