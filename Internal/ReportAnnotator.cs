using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace TestPulse.Internal;

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
/// Reflection-only post-processor: matches [TestPulseCase]-attributed
/// methods in a compiled test assembly to &lt;testcase&gt; entries in a
/// JunitXml.TestLogger-produced report by exact class+method name, and
/// injects testpulse_case_key/_platform/_version/_tags as &lt;property&gt;
/// elements. Never guesses at a decorated (Theory) or ambiguous
/// (overloaded) name -- both are left unmodified with a logged note.
/// </summary>
public static class ReportAnnotator
{
    public static AnnotationResult Annotate(string reportPath, Assembly testAssembly)
    {
        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException(
                $"testpulse: expected JunitXml.TestLogger report at '{reportPath}' but it does not exist. " +
                "Ensure the JunitXml.TestLogger package is referenced and its logger is enabled.",
                reportPath);
        }

        var methodsByClass = CollectMethodsByClass(testAssembly);

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        XDocument doc;
        using (var stream = File.OpenRead(reportPath))
        using (var reader = XmlReader.Create(stream, settings))
        {
            doc = XDocument.Load(reader);
        }

        var matchedCaseKeys = new List<string>();
        var notes = new List<AnnotationNote>();

        foreach (var testcase in doc.Descendants("testcase"))
        {
            var className = (string?)testcase.Attribute("classname");
            var name = (string?)testcase.Attribute("name");
            if (className is null || name is null)
            {
                continue;
            }
            if (!methodsByClass.TryGetValue(className, out var candidates))
            {
                continue;
            }

            var exact = candidates.Where(m => m.Name == name).ToList();
            if (exact.Count == 1)
            {
                var attr = exact[0].GetCustomAttribute<TestPulseCaseAttribute>();
                if (attr is not null)
                {
                    InjectProperties(testcase, attr);
                    matchedCaseKeys.Add(attr.CaseKey);
                }
                // else: an untagged method matched exactly -- nothing to do, not an error.
            }
            else if (exact.Count > 1)
            {
                if (exact.Any(m => m.GetCustomAttribute<TestPulseCaseAttribute>() is not null))
                {
                    notes.Add(new AnnotationNote { ClassName = className, TestCaseName = name, Reason = "ambiguous: class+name matches more than one method (an overload)" });
                }
            }
            else
            {
                var decorated = candidates.FirstOrDefault(m =>
                    name.StartsWith(m.Name + "(", StringComparison.Ordinal) &&
                    m.GetCustomAttribute<TestPulseCaseAttribute>() is not null);
                if (decorated is not null)
                {
                    notes.Add(new AnnotationNote { ClassName = className, TestCaseName = name, Reason = $"skipped: '{decorated.Name}' is a [Theory] invocation, unsupported for property injection" });
                }
            }
        }

        var writerSettings = new XmlWriterSettings { Indent = true };
        using (var stream = File.Create(reportPath))
        using (var writer = XmlWriter.Create(stream, writerSettings))
        {
            doc.Save(writer);
        }

        return new AnnotationResult { MatchedCaseKeys = matchedCaseKeys, Notes = notes };
    }

    // Collects ALL methods per class (not just [TestPulseCase]-tagged
    // ones) -- ambiguity (an overload sharing a name with a tagged
    // method) can only be detected by seeing every candidate a
    // <testcase name> could refer to, not just the tagged ones.
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
            result[type.FullName ?? type.Name] = methods.ToList();
        }
        return result;
    }

    private static void InjectProperties(XElement testcase, TestPulseCaseAttribute attr)
    {
        var properties = testcase.Element("properties");
        if (properties is null)
        {
            properties = new XElement("properties");
            testcase.AddFirst(properties);
        }

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
        properties.Add(new XElement("property",
            new XAttribute("name", name),
            new XAttribute("value", value)));
    }
}
