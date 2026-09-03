namespace TestPulse.MSBuild.Tests.Fixtures;

// Pure reflection fixtures for ReportAnnotator tests -- deliberately NOT
// real xUnit [Fact]s (no attribute needed for that), since these classes
// exist only to be reflected over, not executed.
public class LoginTests
{
    [TestPulse.TestPulseCase("LOGIN-42")]
    public void LoginSucceeds() { }

    [TestPulse.TestPulseCase("LOGIN-43", platform: "linux", tags: new[] { "smoke" })]
    public void LoginWithPlatformAndTags() { }

    [TestPulse.TestPulseCase("LOGIN-44", version: "2.0")]
    public void LoginWithVersion() { }

    public void UntaggedMethod() { }

    [TestPulse.TestPulseCase("THEORY-1")]
    public void DecoratedMethod() { }

    [TestPulse.TestPulseCase("OVERLOAD-1")]
    public void OverloadedMethod() { }

    public void OverloadedMethod(int x) { }
}
