using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using TestPulse.Internal;

namespace TestPulse;

/// <summary>
/// The VSTest logger extension that triggers annotate+submit -- chosen
/// over an AfterTargets="VSTest" MSBuild target after a real repro
/// showed that approach is silently never scheduled when the underlying
/// test run fails (MSBuild aborts the rest of the requested target graph
/// once VSTestTask itself fails). VSTest invokes every registered
/// logger's TestRunComplete handler when the run finishes, pass or fail.
///
/// Builds its own JUnit XML report directly from the TestResult events
/// it observes during the run, rather than depending on a separately
/// registered JunitXml.TestLogger -- discovered during implementation
/// that VSTest's $(VSTestLogger) MSBuild property cannot combine two
/// independently-named loggers into a single run (each works alone, but
/// "junit;testpulse" silently drops one), a real platform limitation,
/// not something fixable at the MSBuild level. Building the report
/// directly avoids the problem entirely.
/// </summary>
[FriendlyName("testpulse")]
[ExtensionUri("logger://TestPulse/TestPulseLogger/v1")]
public sealed class TestPulseLogger : ITestLogger
{
    private string? _testAssemblyPath;
    private readonly List<ObservedResult> _results = new();

    // The VSTest logger runs inside the "driver" process (dotnet/vstest.console),
    // not the separate test host process Attach() calls run in -- confirmed
    // empirically that AppContext.BaseDirectory in the driver process resolves
    // to the SDK's own install directory, not the test project's build output.
    // The test assembly's own path (handed to us directly via TestResultEventArgs,
    // not derived from either process's notion of "base directory") is the one
    // reliable, process-independent anchor both this logger and Attach() (in the
    // test host process) can agree on.
    private string TestOutputDir => Path.GetDirectoryName(_testAssemblyPath!)!;

    public void Initialize(TestLoggerEvents events, string testResultsDirPath)
    {
        events.TestResult += OnTestResult;
        events.TestRunComplete += OnTestRunComplete;
    }

    private void OnTestResult(object? sender, TestResultEventArgs e)
    {
        _testAssemblyPath ??= e.Result.TestCase.Source;

        var outcome = e.Result.Outcome switch
        {
            TestOutcome.Passed => ObservedOutcome.Passed,
            TestOutcome.Failed => ObservedOutcome.Failed,
            TestOutcome.Skipped => ObservedOutcome.Skipped,
            _ => ObservedOutcome.Passed,
        };

        var (className, methodName) = SplitFullyQualifiedName(e.Result.TestCase.FullyQualifiedName);

        _results.Add(new ObservedResult(
            className,
            methodName,
            e.Result.DisplayName ?? e.Result.TestCase.DisplayName,
            e.Result.Duration,
            outcome,
            e.Result.ErrorMessage,
            e.Result.ErrorStackTrace));
    }

    private static (string ClassName, string MethodName) SplitFullyQualifiedName(string fqn)
    {
        var lastDot = fqn.LastIndexOf('.');
        return lastDot < 0 ? ("", fqn) : (fqn[..lastDot], fqn[(lastDot + 1)..]);
    }

    private void OnTestRunComplete(object? sender, TestRunCompleteEventArgs e)
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            // A logger's own exceptions don't propagate to fail
            // `dotnet test`'s exit code (VSTest isolates logger
            // failures) -- print clearly so the failure is at least
            // visible, and write a result marker so the AfterTargets
            // build-failure check (see TestPulse.MSBuild.targets) can
            // still fail the build in the common case (tests passed).
            Console.Error.WriteLine($"testpulse: unexpected error: {ex}");
            WriteResultMarker(failed: true);
        }
    }

    private void Run()
    {
        if (_testAssemblyPath is null || _results.Count == 0)
        {
            return;
        }

        var config = Config.Resolve(Environment.GetEnvironmentVariable);

        if (config.Url is null || config.Token is null || config.Project is null)
        {
            Console.Error.WriteLine("testpulse: TESTPULSE_URL, TESTPULSE_TOKEN, and TESTPULSE_PROJECT are required (set directly, or via TestPulseUrl/TestPulseToken/TestPulseProject MSBuild properties). Skipping submission.");
            return;
        }

        var assembly = Assembly.LoadFrom(_testAssemblyPath);
        var suiteName = Path.GetFileNameWithoutExtension(_testAssemblyPath);
        var (reportXml, annotation) = JUnitReportBuilder.Build(suiteName, assembly, _results);

        foreach (var note in annotation.Notes)
        {
            Console.WriteLine($"testpulse: {note.ClassName}.{note.TestCaseName}: {note.Reason}");
        }

        using var client = new TestPulseClient(config.Url, config.Token);

        if (config.DryRun)
        {
            RunDryRun(client, config, annotation).GetAwaiter().GetResult();
            return;
        }

        RunSubmit(client, config, reportXml).GetAwaiter().GetResult();
    }

    private async Task RunDryRun(TestPulseClient client, Config config, AnnotationResult annotation)
    {
        Console.WriteLine("testpulse: dry run -- no import will be submitted");
        try
        {
            var existing = await client.ListCaseKeysAsync(config.Project!);
            var existingSet = new HashSet<string>(existing);
            foreach (var key in annotation.MatchedCaseKeys)
            {
                Console.WriteLine(existingSet.Contains(key)
                    ? $"  would match: {key}"
                    : $"  would NOT match (no such case): {key}");
            }
            WriteResultMarker(failed: false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"testpulse: dry-run fetch failed: {ex.Message}");
            WriteResultMarker(failed: true);
        }
    }

    private async Task RunSubmit(TestPulseClient client, Config config, string reportXml)
    {
        var attachments = AttachmentReader.ReadAll(Path.Combine(TestOutputDir, ".testpulse", "attachments"));
        var newAttachments = new List<NewAttachment>();
        foreach (var a in attachments)
        {
            newAttachments.Add(new NewAttachment(a.CaseKey, a.Filename, a.ContentType, Convert.ToBase64String(a.Data)));
        }

        try
        {
            var outcome = await client.SubmitImportAsync(config.Project!, new ImportRequest("junit-xml", reportXml, newAttachments));

            switch (outcome.StatusCode)
            {
                case 201:
                    Console.WriteLine($"testpulse: all tests matched, created run {outcome.Run?.Key}");
                    WriteResultMarker(failed: false);
                    break;
                case 207:
                    var unmatched = outcome.Result?.Unmatched ?? new List<UnmatchedTest>();
                    Console.WriteLine($"testpulse: {outcome.Result?.Matched} matched, {unmatched.Count} unmatched (run {outcome.Result?.Run?.Key})");
                    foreach (var u in unmatched)
                    {
                        Console.WriteLine($"  unmatched: {u.CaseKey}");
                    }
                    if (unmatched.Count > 0)
                    {
                        Console.WriteLine("testpulse: use TestPulseFailOnUnmatched to make this a hard failure");
                        WriteResultMarker(failed: config.FailOnUnmatched);
                    }
                    else
                    {
                        WriteResultMarker(failed: false);
                    }
                    break;
                default:
                    Console.Error.WriteLine($"testpulse: submission failed: status {outcome.StatusCode}: {outcome.ErrorBody}");
                    WriteResultMarker(failed: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"testpulse: submission failed: {ex.Message}");
            WriteResultMarker(failed: true);
        }
    }

    private void WriteResultMarker(bool failed)
    {
        try
        {
            var dir = Path.Combine(TestOutputDir, ".testpulse");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "result.json"), JsonSerializer.Serialize(new { failed }));
        }
        catch
        {
            // Best-effort -- if we can't even write the marker, the
            // AfterTargets check simply won't find one and won't fail
            // the build on our behalf; the console output above is the
            // durable record either way.
        }
    }
}
