using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TestPulse.Internal;
using Xunit;

namespace TestPulse.MSBuild.Tests;

public class JUnitReportBuilderTests
{
    private static readonly Assembly FixtureAssembly = typeof(Fixtures.LoginTests).Assembly;
    private const string FixtureClass = "TestPulse.MSBuild.Tests.Fixtures.LoginTests";

    private static ObservedResult Passed(string method) =>
        new(FixtureClass, method, method, TimeSpan.FromMilliseconds(5), ObservedOutcome.Passed, null, null);

    private static XElement Testcase(string xml, string name)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("testcase").First(e => (string)e.Attribute("name")! == name);
    }

    [Fact]
    public void InjectsCaseKeyPropertyForATaggedMethod()
    {
        var (xml, result) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { Passed("LoginSucceeds") });

        var testcase = Testcase(xml, "LoginSucceeds");
        var prop = testcase.Element("properties")!.Elements("property")
            .First(p => (string)p.Attribute("name")! == "testpulse_case_key");
        Assert.Equal("LOGIN-42", (string)prop.Attribute("value")!);
        Assert.Contains("LOGIN-42", result.MatchedCaseKeys);
    }

    [Fact]
    public void RecordsOptionalMetadataOnlyWhenSupplied()
    {
        var (xml, _) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { Passed("LoginWithPlatformAndTags") });

        var testcase = Testcase(xml, "LoginWithPlatformAndTags");
        var names = testcase.Element("properties")!.Elements("property").Select(p => (string)p.Attribute("name")!).ToList();
        Assert.Contains("testpulse_platform", names);
        Assert.Contains("testpulse_tags", names);
        Assert.DoesNotContain("testpulse_version", names);
    }

    [Fact]
    public void UntaggedMethodCarriesNoTestPulseProperties()
    {
        var (xml, _) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { Passed("UntaggedMethod") });

        var testcase = Testcase(xml, "UntaggedMethod");
        Assert.Null(testcase.Element("properties"));
    }

    [Fact]
    public void DecoratedTheoryInvocationIsLeftUnmodifiedAndNoted()
    {
        var (xml, result) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[]
        {
            new ObservedResult(FixtureClass, "DecoratedMethod(user: \"a\")", "DecoratedMethod(user: \"a\")", TimeSpan.Zero, ObservedOutcome.Passed, null, null),
        });

        var testcase = Testcase(xml, "DecoratedMethod(user: \"a\")");
        Assert.Null(testcase.Element("properties"));
        Assert.Contains(result.Notes, n => n.TestCaseName.StartsWith("DecoratedMethod") && n.Reason.Contains("Theory"));
    }

    [Fact]
    public void AmbiguousOverloadIsLeftUnmodifiedAndNoted()
    {
        var (xml, result) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { Passed("OverloadedMethod") });

        var testcase = Testcase(xml, "OverloadedMethod");
        Assert.Null(testcase.Element("properties"));
        Assert.Contains(result.Notes, n => n.TestCaseName == "OverloadedMethod" && n.Reason.Contains("ambiguous"));
    }

    [Fact]
    public void FailedResultIncludesAFailureElement()
    {
        var failed = new ObservedResult(FixtureClass, "LoginSucceeds", "LoginSucceeds", TimeSpan.Zero, ObservedOutcome.Failed, "boom", "at Foo.Bar()");
        var (xml, _) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { failed });

        var testcase = Testcase(xml, "LoginSucceeds");
        var failure = testcase.Element("failure");
        Assert.NotNull(failure);
        Assert.Equal("boom", (string)failure!.Attribute("message")!);
    }

    [Fact]
    public void SkippedResultIncludesASkippedElement()
    {
        var skipped = new ObservedResult(FixtureClass, "LoginSucceeds", "LoginSucceeds", TimeSpan.Zero, ObservedOutcome.Skipped, null, null);
        var (xml, _) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { skipped });

        var testcase = Testcase(xml, "LoginSucceeds");
        Assert.NotNull(testcase.Element("skipped"));
    }
}
