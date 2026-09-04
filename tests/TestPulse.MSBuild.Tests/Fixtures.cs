using Xunit;

namespace TestPulse.MSBuild.Tests.Fixtures;

// Pure reflection fixtures for JUnitReportBuilder tests -- deliberately
// NOT run as real xUnit tests themselves (the [Theory]/[InlineData] below
// exist only so GetCustomAttribute<TheoryAttribute>() finds something
// real to reflect over, matching how TryInjectProperties actually
// detects a Theory method -- see JUnitReportBuilder.cs).
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
    [Theory]
    [InlineData("a")]
    public void DecoratedMethod(string user) { }

    [TestPulse.TestPulseCase("OVERLOAD-1")]
    public void OverloadedMethod() { }

    public void OverloadedMethod(int x) { }
}
