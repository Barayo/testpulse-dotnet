using System.IO;
using System.Linq;
using Xunit;

namespace TestPulse.MSBuild.Tests;

public class ParallelAttachE2ETests
{
    [Fact]
    public void ConcurrentAttachCallsFromDifferentCollectionsDoNotCorruptEachOther()
    {
        var fixtureDir = Path.Combine(E2EHelper.RepoRoot, "tests", "fixtures", "ParallelAttachFixture");
        // The test host process's own CWD is the build output directory
        // (bin/Debug/net8.0/), not the project source directory --
        // confirmed empirically. This is what TestPulseAttachments.ScratchDir()
        // resolves against in the real (in-process) case too, so it's
        // self-consistent for the actual plugin; this outer harness just
        // needs to know where the *child* process's CWD really is.
        var scratchDir = Path.Combine(fixtureDir, "bin", "Debug", "net8.0", ".testpulse", "attachments");
        if (Directory.Exists(scratchDir))
        {
            Directory.Delete(scratchDir, recursive: true);
        }

        var (exitCode, output) = E2EHelper.RunDotnetTest("ParallelAttachFixture");
        Assert.True(exitCode == 0, $"dotnet test failed unexpectedly:\n{output}");

        Assert.True(Directory.Exists(scratchDir), $"expected scratch dir at {scratchDir}");
        var dataFiles = Directory.GetFiles(scratchDir, "*.data");
        Assert.Equal(2, dataFiles.Length);

        var jsonFiles = Directory.GetFiles(scratchDir, "*.json");
        var caseKeys = jsonFiles.Select(File.ReadAllText).ToList();
        Assert.Contains(caseKeys, j => j.Contains("PARALLEL-A"));
        Assert.Contains(caseKeys, j => j.Contains("PARALLEL-B"));

        Directory.Delete(scratchDir, recursive: true);
    }
}
