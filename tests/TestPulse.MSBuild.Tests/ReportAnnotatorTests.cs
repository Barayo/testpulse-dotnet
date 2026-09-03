using System.IO;
using System.Reflection;
using System.Xml.Linq;
using TestPulse.Internal;
using Xunit;

namespace TestPulse.MSBuild.Tests;

public class ReportAnnotatorTests
{
    private static readonly Assembly FixtureAssembly = typeof(Fixtures.LoginTests).Assembly;
    private const string FixtureClass = "TestPulse.MSBuild.Tests.Fixtures.LoginTests";

    private static string WriteReport(string xml)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, xml);
        return path;
    }

    private static XElement ReadTestcase(string reportPath, string name)
    {
        var doc = XDocument.Load(reportPath);
        return doc.Descendants("testcase").First(e => (string)e.Attribute("name")! == name);
    }

    [Fact]
    public void InjectsCaseKeyPropertyForATaggedMethod()
    {
        var path = WriteReport($"""
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="LoginSucceeds" />
              </testsuite>
            </testsuites>
            """);

        var result = ReportAnnotator.Annotate(path, FixtureAssembly);

        var testcase = ReadTestcase(path, "LoginSucceeds");
        var prop = testcase.Element("properties")!.Elements("property")
            .First(p => (string)p.Attribute("name")! == "testpulse_case_key");
        Assert.Equal("LOGIN-42", (string)prop.Attribute("value")!);
        Assert.Contains("LOGIN-42", result.MatchedCaseKeys);
    }

    [Fact]
    public void RecordsOptionalMetadataOnlyWhenSupplied()
    {
        var path = WriteReport($"""
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="LoginWithPlatformAndTags" />
              </testsuite>
            </testsuites>
            """);

        ReportAnnotator.Annotate(path, FixtureAssembly);

        var testcase = ReadTestcase(path, "LoginWithPlatformAndTags");
        var names = testcase.Element("properties")!.Elements("property")
            .Select(p => (string)p.Attribute("name")!).ToList();
        Assert.Contains("testpulse_platform", names);
        Assert.Contains("testpulse_tags", names);
        Assert.DoesNotContain("testpulse_version", names);
    }

    [Fact]
    public void UntaggedMethodCarriesNoTestPulseProperties()
    {
        var path = WriteReport($"""
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="UntaggedMethod" />
              </testsuite>
            </testsuites>
            """);

        ReportAnnotator.Annotate(path, FixtureAssembly);

        var testcase = ReadTestcase(path, "UntaggedMethod");
        Assert.Null(testcase.Element("properties"));
    }

    [Fact]
    public void DecoratedTheoryInvocationIsLeftUnmodifiedAndNoted()
    {
        var path = WriteReport($"""
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="DecoratedMethod(user: &quot;a&quot;)" />
              </testsuite>
            </testsuites>
            """);

        var result = ReportAnnotator.Annotate(path, FixtureAssembly);

        var testcase = ReadTestcase(path, "DecoratedMethod(user: \"a\")");
        Assert.Null(testcase.Element("properties"));
        Assert.Contains(result.Notes, n => n.TestCaseName.StartsWith("DecoratedMethod") && n.Reason.Contains("Theory"));
    }

    [Fact]
    public void AmbiguousOverloadIsLeftUnmodifiedAndNoted()
    {
        var path = WriteReport($"""
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="OverloadedMethod" />
              </testsuite>
            </testsuites>
            """);

        var result = ReportAnnotator.Annotate(path, FixtureAssembly);

        var testcase = ReadTestcase(path, "OverloadedMethod");
        Assert.Null(testcase.Element("properties"));
        Assert.Contains(result.Notes, n => n.TestCaseName == "OverloadedMethod" && n.Reason.Contains("ambiguous"));
    }

    [Fact]
    public void MissingReportFileThrowsWithNamedPath()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "testpulse-does-not-exist-" + Path.GetRandomFileName() + ".xml");

        var ex = Assert.Throws<FileNotFoundException>(() => ReportAnnotator.Annotate(missingPath, FixtureAssembly));

        Assert.Contains(missingPath, ex.Message);
    }

    [Fact]
    public void ExternalEntityInReportIsNotExpanded()
    {
        var path = WriteReport($"""
            <?xml version="1.0"?>
            <!DOCTYPE testsuites [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <testsuites>
              <testsuite name="s">
                <testcase classname="{FixtureClass}" name="LoginSucceeds">&xxe;</testcase>
              </testsuite>
            </testsuites>
            """);

        Assert.ThrowsAny<System.Xml.XmlException>(() => ReportAnnotator.Annotate(path, FixtureAssembly));
    }
}
