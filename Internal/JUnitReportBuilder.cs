using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace TestPulse.Internal;

public sealed record ObservedResult(
    string ClassName,
    string MethodName,
    string DisplayName,
    TimeSpan Duration,
    ObservedOutcome Outcome,
    string? ErrorMessage,
    string? ErrorStackTrace);

public enum ObservedOutcome { Passed, Failed, Skipped }

public sealed class AnnotationNote
{
    public required string ClassName { get; init; }
    public required string TestCaseName { get; init; }
    public required string Reason { get; init; }
}

public sealed class AnnotationResult
{
    public required List<string> MatchedCaseKeys { get; init; }
    public required List<AnnotationNote> Notes { get; init; }
}

/// <summary>
/// Builds a JUnit XML report directly from the TestResult events a
/// VSTest logger already observes during the run, with
/// testpulse_case_key/_platform/_version/_tags injected at construction
/// time -- rather than depending on JunitXml.TestLogger's own separately
/// written file. This replaced an earlier design (ReportAnnotator reading
/// and rewriting JunitXml.TestLogger's output) after discovering, during
/// implementation, that VSTest's `$(VSTestLogger)` MSBuild property
/// cannot combine two independently-named loggers into one run (each
/// name works alone; "junit;testpulse" silently drops one) -- a real,
/// confirmed platform limitation, not something this plugin can work
/// around at the MSBuild level. Building the report directly removes the
/// dependency entirely, along with the multi-logger problem and the
/// "does the other logger's file already exist" ordering risk that
/// design was carrying.
/// </summary>
public static class JUnitReportBuilder
{
    public static (string Xml, AnnotationResult Annotation) Build(string suiteName, Assembly testAssembly, IReadOnlyList<ObservedResult> results)
    {
        var methodsByClass = CollectMethodsByClass(testAssembly);
        var matchedCaseKeys = new List<string>();
        var notes = new List<AnnotationNote>();

        var testsuite = new XElement("testsuite",
            new XAttribute("name", suiteName),
            new XAttribute("tests", results.Count),
            new XAttribute("failures", results.Count(r => r.Outcome == ObservedOutcome.Failed)),
            new XAttribute("errors", 0),
            new XAttribute("skipped", results.Count(r => r.Outcome == ObservedOutcome.Skipped)));

        foreach (var result in results)
        {
            var testcase = new XElement("testcase",
                new XAttribute("classname", result.ClassName),
                new XAttribute("name", result.MethodName),
                new XAttribute("time", result.Duration.TotalSeconds.ToString("F6")));

            if (result.Outcome == ObservedOutcome.Failed)
            {
                testcase.Add(new XElement("failure",
                    new XAttribute("message", result.ErrorMessage ?? "test failed"),
                    result.ErrorStackTrace ?? ""));
            }
            else if (result.Outcome == ObservedOutcome.Skipped)
            {
                testcase.Add(new XElement("skipped"));
            }

            TryInjectProperties(testcase, methodsByClass, result.ClassName, result.MethodName, matchedCaseKeys, notes);

            testsuite.Add(testcase);
        }

        var doc = new XElement("testsuites", testsuite);
        var sb = new StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { Indent = true, OmitXmlDeclaration = false }))
        {
            new XDocument(doc).Save(writer);
        }

        return (sb.ToString(), new AnnotationResult { MatchedCaseKeys = matchedCaseKeys, Notes = notes });
    }

    private static void TryInjectProperties(
        XElement testcase,
        Dictionary<string, List<MethodInfo>> methodsByClass,
        string className,
        string name,
        List<string> matchedCaseKeys,
        List<AnnotationNote> notes)
    {
        if (!methodsByClass.TryGetValue(className, out var candidates))
        {
            return;
        }

        var exact = candidates.FindAll(m => m.Name == name);
        if (exact.Count == 1)
        {
            var attr = exact[0].GetCustomAttribute<TestPulseCaseAttribute>();
            if (attr is not null)
            {
                InjectProperties(testcase, attr);
                matchedCaseKeys.Add(attr.CaseKey);
            }
        }
        else if (exact.Count > 1)
        {
            if (exact.Exists(m => m.GetCustomAttribute<TestPulseCaseAttribute>() is not null))
            {
                notes.Add(new AnnotationNote { ClassName = className, TestCaseName = name, Reason = "ambiguous: class+name matches more than one method (an overload)" });
            }
        }
        else
        {
            var decorated = candidates.Find(m =>
                name.StartsWith(m.Name + "(", StringComparison.Ordinal) &&
                m.GetCustomAttribute<TestPulseCaseAttribute>() is not null);
            if (decorated is not null)
            {
                notes.Add(new AnnotationNote { ClassName = className, TestCaseName = name, Reason = $"skipped: '{decorated.Name}' is a [Theory] invocation, unsupported for property injection" });
            }
        }
    }

    private static void InjectProperties(XElement testcase, TestPulseCaseAttribute attr)
    {
        var properties = new XElement("properties");
        testcase.AddFirst(properties);
        AddProperty(properties, "testpulse_case_key", attr.CaseKey);
        if (!string.IsNullOrEmpty(attr.Platform))
        {
            AddProperty(properties, "testpulse_platform", attr.Platform!);
        }
        if (!string.IsNullOrEmpty(attr.Version))
        {
            AddProperty(properties, "testpulse_version", attr.Version!);
        }
        if (attr.Tags is { Length: > 0 })
        {
            AddProperty(properties, "testpulse_tags", string.Join(",", attr.Tags));
        }
    }

    private static void AddProperty(XElement properties, string name, string value)
    {
        properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private static Dictionary<string, List<MethodInfo>> CollectMethodsByClass(Assembly assembly)
    {
        var result = new Dictionary<string, List<MethodInfo>>();
        foreach (var type in assembly.GetTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (methods.Length == 0)
            {
                continue;
            }
            result[type.FullName ?? type.Name] = new List<MethodInfo>(methods);
        }
        return result;
    }
}

internal static class ListExtensions
{
    public static int Count(this IReadOnlyList<ObservedResult> list, Func<ObservedResult, bool> predicate)
    {
        var count = 0;
        foreach (var item in list)
        {
            if (predicate(item))
            {
                count++;
            }
        }
        return count;
    }
}
