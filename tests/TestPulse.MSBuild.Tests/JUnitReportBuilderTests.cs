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
    public void TheoryTaggedMethodIsLeftUnmodifiedAndNoted()
    {
        // VSTest's real TestCase.FullyQualifiedName does NOT carry a
        // Theory invocation's decorated suffix (only DisplayName does,
        // confirmed empirically against a real xUnit v3 Theory run) --
        // every invocation of a [Theory] method reaches the annotate step
        // under the exact same undecorated name, so two rows sharing one
        // name is what a real multi-InlineData Theory run actually looks
        // like, not a single decorated name.
        var (xml, result) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[]
        {
            Passed("DecoratedMethod"),
            Passed("DecoratedMethod"),
        });

        var testcases = XDocument.Parse(xml).Descendants("testcase")
            .Where(e => (string)e.Attribute("name")! == "DecoratedMethod").ToList();
        Assert.Equal(2, testcases.Count);
        Assert.All(testcases, tc => Assert.Null(tc.Element("properties")));
        Assert.Contains(result.Notes, n => n.TestCaseName == "DecoratedMethod" && n.Reason.Contains("Theory"));
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

    [Fact]
    public void ReportDoesNotDeclareAMismatchedEncoding()
    {
        // XmlWriter.Create(StringBuilder, ...) always emits
        // encoding="utf-16" in the prolog regardless of the actual bytes
        // -- a well-known .NET gotcha. The report is only ever embedded as
        // a string inside a JSON payload sent as UTF-8, so a declared
        // "utf-16" is simply wrong and TestPulse's server-side decoder
        // (no CharsetReader configured) rejects it outright with a real
        // 400. Confirmed against a real running TestPulse instance.
        var (xml, _) = JUnitReportBuilder.Build("suite", FixtureAssembly, new[] { Passed("LoginSucceeds") });

        Assert.DoesNotContain("utf-16", xml, StringComparison.OrdinalIgnoreCase);
    }
}
