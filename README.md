# testpulse-dotnet

A NuGet package for reporting `dotnet test` results into
[TestPulse](https://github.com/Barayo/TestPulse) — tags an xUnit v3 test
with a TestPulse case key, and auto-annotates + submits a JUnit XML
report on a plain `dotnet test`, no CLI flags required.

`dotnet test` has no built-in JUnit XML output, and the de facto standard
tool for producing one (`JunitXml.TestLogger`) doesn't support writing
`<properties>` into the report. `TestPulseLogger`, a custom VSTest logger
this package auto-registers, builds its own JUnit XML report directly
from the test run instead, injecting `testpulse_case_key` (and optional
`testpulse_platform`/`testpulse_version`/`testpulse_tags`) properties for
every `[TestPulseCase]`-tagged method, then submits it.

> **Requires `xunit.v3`** (not `xunit` v2) — attachments are identified
> via xUnit v3's own `TestContext.Current`, an ambient test-identity
> mechanism xUnit v2 doesn't have.

## Install

```sh
dotnet add package TestPulse.MSBuild
```

**If you're starting a new test project, `dotnet new xunit` scaffolds
xUnit v2 by default — this package requires v3.** Use
`dotnet new xunit -f net8.0 --xunit-version 3` instead, or if you already
have a v2 project, swap `xunit`/`xunit.runner.visualstudio` for
`xunit.v3`. `Attach()` throws "outside an active TestPulse test
execution" on xUnit v2, even from a genuinely running test, since v2 has
no `TestContext.Current` — this is expected, not a bug, but easy to hit
by starting from the default template.

## Tag your tests

```csharp
using TestPulse;
using Xunit;

public class LoginTests
{
    [TestPulseCase(caseKey: "LOGIN-42", platform: "linux", tags: new[] { "smoke" })]
    [Fact]
    public void LoginSucceeds()
    {
        // ...
    }
}
```

Run your suite normally — no extra flags:

```sh
dotnet test
```

`TestPulseLogger` is auto-registered by the package's own `.props` file
(`<VSTestLogger>`/`<VSTestTestAdapterPath>`, guarded so an existing
explicit `VSTestLogger` choice is never overridden) and runs regardless
of whether the tests themselves passed or failed.

`[Theory]` (parameterized) methods are unsupported for tagging: a
Theory invocation's decorated `<testcase>` name (e.g.
`LoginSucceeds(user: "a")`) never receives a property, so a
`[TestPulseCase]` on a `[Theory]` method is a documented no-op — a note
is logged naming it.

## Attach screenshots/files

```csharp
[TestPulseCase(caseKey: "LOGIN-43")]
[Fact]
public void LoginFailsWithBadPassword()
{
    var screenshot = TakeScreenshot();
    TestPulseAttachments.Attach("LOGIN-43", screenshot, "failure.png", "image/png");
}
```

`Attach` only accepts a case key the *currently-executing* test method
has itself declared via `[TestPulseCase]` (identified through xUnit v3's
`TestContext.Current.TestMethod`) — an attachment under a case key
belonging to a different test method throws, as does a call made outside
an active test execution (e.g. from a constructor or an un-awaited
background task). Only `image/png`, `image/jpeg`, and `image/webp` are
accepted. Multiple `Attach` calls under the same case key within one test
are all preserved (e.g. capturing two screenshots). Ambient test identity
correctly survives `async`/`await` continuations that resume on a
different thread, and concurrent `Attach` calls from different test
classes (xUnit's default parallelization unit is the test collection, and
a single class is one collection) don't corrupt each other's attachments.

## Configuration

`TestPulseLogger` reads plain environment variables. Url/Project/
FailOnUnmatched/DryRun can be set either as real environment variables
directly, or as MSBuild properties in your `.csproj` (or a `/p:` override)
— the package's `.props`/`.targets` files export the MSBuild property
values as real environment variables on the process before `dotnet test`
spawns the test run, only when the variable isn't already set directly,
so a value you set directly (e.g. a CI secret) is never overwritten.

| Setting | MSBuild property (csproj default or `/p:` override) | Environment variable |
|---|---|---|
| API base URL | `<TestPulseUrl>` | `TESTPULSE_URL` |
| Project key | `<TestPulseProject>` | `TESTPULSE_PROJECT` |
| Fail on unmatched | `<TestPulseFailOnUnmatched>` | `TESTPULSE_FAIL_ON_UNMATCHED` |
| Dry run | `<TestPulseDryRun>` | `TESTPULSE_DRY_RUN` |
| API token | *(none — see below)* | `TESTPULSE_TOKEN` |

```xml
<!-- your test project's .csproj -->
<PropertyGroup>
  <TestPulseUrl>http://localhost:8080</TestPulseUrl>
  <TestPulseProject>LOGIN</TestPulseProject>
</PropertyGroup>
```

```sh
dotnet test /p:TestPulseUrl=https://ci.example
```

**`TESTPULSE_TOKEN` must be set directly as a real environment variable
(e.g. a CI secret) — there is no MSBuild-property equivalent for it at
all**, unlike the four settings above: a committed csproj property or a
command-line value can end up in shell history or a committed file, so
the token deliberately has no path through either. The resolved token is
never logged, at any verbosity level.

## Build outcome policy

| Response | Behavior |
|---|---|
| `201` all matched | build succeeds; summary logged |
| `207` some unmatched | build succeeds by default (unmatched keys logged, points at `TestPulseFailOnUnmatched`); fails the build if it's enabled |
| network/auth/4xx/5xx error | always fails the build, unconditionally |

A VSTest logger has no way to affect `dotnet test`'s exit code by itself,
so build-failure propagation is a narrow, separate `AfterTargets="VSTest"`
MSBuild target (also packaged) that reads a small result marker
`TestPulseLogger` writes and fails the build via an `<Error>` task if it
indicates failure.

## Dry run

```xml
<TestPulseDryRun>true</TestPulseDryRun>
```

Fetches existing case keys via a read-only
`GET /api/v1/projects/{projectKey}/cases` and previews which tagged tests
would match, without submitting anything. The build outcome is
unaffected by the preview's result.

## License

MIT
