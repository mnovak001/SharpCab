namespace SharpCab;

internal sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public void EnsureSuccess(string fileName, IReadOnlyList<string> arguments)
    {
        if (ExitCode == 0)
            return;

        throw new InvalidOperationException(
            $"Process '{fileName} {string.Join(" ", arguments)}' failed with exit code {ExitCode}.{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{StandardError}{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{StandardOutput}");
    }
}