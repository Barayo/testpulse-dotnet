using System.Collections.Generic;
using Xunit;

namespace TestPulse.MSBuild.Tests;

public class PipelineE2ETests
{
    [Fact]
    public void AllMatchedRealSubmissionSucceedsWithZeroCliFlags()
    {
        using var server = new StubServer
        {
            Handler = path => (201, "{\"id\":\"r1\",\"key\":\"LOGIN-R1\"}"),
        };

        var env = new Dictionary<string, string>
        {
            ["TESTPULSE_URL"] = server.Url.TrimEnd('/'),
            ["TESTPULSE_TOKEN"] = "t0k3n",
            ["TESTPULSE_PROJECT"] = "LOGIN",
        };

        // No CLI flags at all -- proves the whole auto-wiring chain
        // (VSTestLogger/VSTestTestAdapterPath from the .props file,
        // TestPulseLogger's discovery, annotation, and submission) works
        // end to end with a plain `dotnet test`.
        var (exitCode, output) = E2EHelper.RunDotnetTest("BasicFixture", env);

        Assert.True(exitCode == 0, $"dotnet test failed unexpectedly:\n{output}");
        Assert.Contains("LOGIN-R1", output);
        Assert.Equal("/api/v1/projects/LOGIN/imports", server.LastRequestPath);
        Assert.Contains("testpulse_case_key", server.LastRequestBody);
        Assert.Contains("LOGIN-42", server.LastRequestBody);
    }

    [Fact]
    public void SubmissionStillRunsWhenTheUnderlyingTestGenuinelyFails()
    {
        using var server = new StubServer
        {
            Handler = path => (201, "{\"id\":\"r1\",\"key\":\"LOGIN-R2\"}"),
        };

        var env = new Dictionary<string, string>
        {
            ["TESTPULSE_URL"] = server.Url.TrimEnd('/'),
            ["TESTPULSE_TOKEN"] = "t0k3n",
            ["TESTPULSE_PROJECT"] = "LOGIN",
        };

        var (exitCode, output) = E2EHelper.RunDotnetTest("FailingFixture", env);

        // The test itself genuinely failed, so the build correctly fails --
        // but the real point of this test is that submission still ran.
        Assert.True(exitCode != 0, "expected dotnet test to fail because the underlying test failed");
        Assert.Equal("/api/v1/projects/LOGIN/imports", server.LastRequestPath);
        Assert.Contains("LOGIN-42", server.LastRequestBody);
    }

    [Fact]
    public void UnmatchedWithFailOnUnmatchedFailsTheBuildViaTheAfterTargetsCheck()
    {
        using var server = new StubServer
        {
            Handler = path => (207, "{\"run\":{\"id\":\"r1\",\"key\":\"LOGIN-R3\"},\"message\":\"1 unmatched\",\"matched\":0,\"unmatched\":[{\"caseKey\":\"LOGIN-42\",\"verdict\":\"passed\"}]}"),
        };

        var env = new Dictionary<string, string>
        {
            ["TESTPULSE_URL"] = server.Url.TrimEnd('/'),
            ["TESTPULSE_TOKEN"] = "t0k3n",
            ["TESTPULSE_PROJECT"] = "LOGIN",
            ["TESTPULSE_FAIL_ON_UNMATCHED"] = "true",
        };

        var (exitCode, output) = E2EHelper.RunDotnetTest("BasicFixture", env);

        // The underlying test PASSED -- this exit code is entirely due to
        // the AfterTargets result-marker check, proving that mechanism
        // (not the logger itself, which cannot affect exit code) is what
        // makes fail-on-unmatched actually fail the build.
        Assert.True(exitCode != 0, $"expected dotnet test to fail via the AfterTargets check:\n{output}");
    }

    [Fact]
    public void MSBuildPropertyConfigurationReachesTheLoggerWithoutDirectEnvVars()
    {
        // Only TESTPULSE_TOKEN is set directly -- Url/Project are passed
        // as MSBuild property overrides (/p:TestPulseUrl=...), the same
        // mechanism a plain <TestPulseUrl> csproj default resolves
        // through. This is the one configuration path none of the other
        // pipeline tests exercise, and it does NOT work through
        // VSTestEnvironment or embedding values into $(VSTestLogger) --
        // see TestPulse.MSBuild.targets' TestPulseSetEnvVars target for
        // the mechanism that actually delivers it (an inline task setting
        // real environment variables in-process, before VSTestTask spawns
        // the driver subprocess TestPulseLogger runs in).
        using var server = new StubServer
        {
            Handler = path => (201, "{\"id\":\"r1\",\"key\":\"LOGIN-R5\"}"),
        };

        var env = new Dictionary<string, string>
        {
            ["TESTPULSE_TOKEN"] = "t0k3n",
        };

        var (exitCode, output) = E2EHelper.RunDotnetTest(
            "BasicFixture",
            env,
            $"/p:TestPulseUrl={server.Url.TrimEnd('/')}",
            "/p:TestPulseProject=LOGIN");

        Assert.True(exitCode == 0, $"dotnet test failed unexpectedly:\n{output}");
        Assert.Equal("/api/v1/projects/LOGIN/imports", server.LastRequestPath);
    }

    [Fact]
    public void UnmatchedWithoutFailOnUnmatchedLeavesTheBuildSucceeding()
    {
        using var server = new StubServer
        {
            Handler = path => (207, "{\"run\":{\"id\":\"r1\",\"key\":\"LOGIN-R4\"},\"message\":\"1 unmatched\",\"matched\":0,\"unmatched\":[{\"caseKey\":\"LOGIN-42\",\"verdict\":\"passed\"}]}"),
        };

        var env = new Dictionary<string, string>
        {
            ["TESTPULSE_URL"] = server.Url.TrimEnd('/'),
            ["TESTPULSE_TOKEN"] = "t0k3n",
            ["TESTPULSE_PROJECT"] = "LOGIN",
        };

        var (exitCode, output) = E2EHelper.RunDotnetTest("BasicFixture", env);

        Assert.True(exitCode == 0, $"dotnet test failed unexpectedly:\n{output}");
        Assert.Contains("LOGIN-42", output);
    }
}
