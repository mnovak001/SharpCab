namespace SharpCab;

internal sealed record ProcessBinaryRunResult(
    int ExitCode,
    byte[] StandardOutputBytes,
    string StandardError)
{
    public void EnsureSuccess(string fileName, IReadOnlyList<string> arguments)
    {
        if (ExitCode == 0)
            return;

        throw new InvalidOperationException(
            $"Process '{fileName} {string.Join(" ", arguments)}' failed with exit code {ExitCode}.{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{StandardError}");
    }
}