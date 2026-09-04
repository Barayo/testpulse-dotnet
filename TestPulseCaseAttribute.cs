using System;

namespace TestPulse;

/// <summary>
/// Tags an xUnit test method with a TestPulse case key, recorded as a
/// &lt;property&gt; in the JUnit XML report TestPulseLogger builds directly
/// from the test run. Placing this on a [Theory] method is unsupported: a
/// Theory invocation's decorated &lt;testcase&gt; name is left unmodified,
/// so no property is ever injected for it -- a note is logged for this
/// case.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestPulseCaseAttribute : Attribute
{
    public TestPulseCaseAttribute(string caseKey, string? platform = null, string? version = null, string[]? tags = null)
    {
        if (string.IsNullOrWhiteSpace(caseKey))
        {
            throw new ArgumentException("caseKey must not be null or empty.", nameof(caseKey));
        }

        CaseKey = caseKey;
        Platform = platform;
        Version = version;
        Tags = tags;
    }

    public string CaseKey { get; }
    public string? Platform { get; }
    public string? Version { get; }
    public string[]? Tags { get; }
}
