using System.Diagnostics;

namespace SharpBind.Tests;

internal sealed record ProcessResult(int ExitCode, string Output);

internal static class TestProcess
{
    public static ProcessResult RunDotnet(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Reused MSBuild workers can keep redirected output open after the build exits.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet {string.Join(' ', arguments)} did not finish within 60 seconds.");
        }

        return new ProcessResult(process.ExitCode,
            stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }
}
