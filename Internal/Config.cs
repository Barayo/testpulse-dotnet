using System;

namespace TestPulse.Internal;

/// <summary>
/// Resolved from environment variables set on the test host process.
/// TESTPULSE_URL/_TOKEN/_PROJECT/_FAIL_ON_UNMATCHED/_DRY_RUN can be set
/// directly by the user (e.g. a CI secret for the token), or indirectly
/// via the package's own .props file mapping MSBuild properties
/// ($(TestPulseUrl) etc, settable via a plain csproj &lt;PropertyGroup&gt;
/// or a `/p:TestPulseUrl=...` command-line override) into the same
/// environment variables through the real, MSBuild-recognized
/// VSTestEnvironment item -- MSBuild itself resolves the
/// override-vs-csproj-property precedence before the env var is ever
/// set, so there is only one tier to resolve here, not several.
/// </summary>
public sealed class Config
{
    public string? Url { get; init; }
    public string? Token { get; init; }
    public string? Project { get; init; }
    public bool FailOnUnmatched { get; init; }
    public bool DryRun { get; init; }

    public static Config Resolve(Func<string, string?> getEnv)
    {
        return new Config
        {
            Url = NullIfEmpty(getEnv("TESTPULSE_URL")),
            Token = NullIfEmpty(getEnv("TESTPULSE_TOKEN")),
            Project = NullIfEmpty(getEnv("TESTPULSE_PROJECT")),
            FailOnUnmatched = ParseBool(getEnv("TESTPULSE_FAIL_ON_UNMATCHED")),
            DryRun = ParseBool(getEnv("TESTPULSE_DRY_RUN")),
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static bool ParseBool(string? value) =>
        value is not null && bool.TryParse(value, out var b) && b;

    public override string ToString()
    {
        var tokenDisplay = string.IsNullOrEmpty(Token) ? "(not set)" : "(redacted)";
        return $"Config{{Url={Url}, Token={tokenDisplay}, Project={Project}, FailOnUnmatched={FailOnUnmatched}, DryRun={DryRun}}}";
    }
}
