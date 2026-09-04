using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TestPulse.MSBuild.Tests;

internal static class E2EHelper
{
    public static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "TestPulse.MSBuild.csproj")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root (TestPulse.MSBuild.csproj not found in any ancestor directory)");
    }

    public static (int ExitCode, string Output) RunDotnetTest(
        string fixtureRelativePath,
        System.Collections.Generic.IDictionary<string, string>? env = null,
        params string[] extraArgs)
    {
        var fixtureDir = Path.Combine(RepoRoot, "tests", "fixtures", fixtureRelativePath);

        var psi = new ProcessStartInfo("dotnet", BuildArgs(extraArgs))
        {
            WorkingDirectory = fixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (env is not null)
        {
            foreach (var kv in env)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    private static string BuildArgs(string[] extraArgs)
    {
        var sb = new StringBuilder("test");
        foreach (var arg in extraArgs)
        {
            sb.Append(' ').Append(arg);
        }
        return sb.ToString();
    }
}
