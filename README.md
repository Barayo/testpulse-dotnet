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

Settings are read from environment variables on the test host process
(`TestPulseLogger` runs in a separate process from MSBuild's own property
evaluation, so it can't read MSBuild properties directly). The package's
`.props` file bridges MSBuild properties into the same-named environment
variables for you, only when the property is actually set — so an
environment variable you already set directly (e.g. a CI secret) is never
overwritten with an empty value.

| Setting | MSBuild property (csproj default or `/p:` override) | Environment variable |
|---|---|---|
| API base URL | `<TestPulseUrl>` | `TESTPULSE_URL` |
| API token | `<TestPulseToken>` | `TESTPULSE_TOKEN` |
| Project key | `<TestPulseProject>` | `TESTPULSE_PROJECT` |
| Fail on unmatched | `<TestPulseFailOnUnmatched>` | `TESTPULSE_FAIL_ON_UNMATCHED` |
| Dry run | `<TestPulseDryRun>` | `TESTPULSE_DRY_RUN` |

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

**Set `TESTPULSE_TOKEN` directly in your environment (e.g. a CI secret),
not `<TestPulseToken>`/`/p:TestPulseToken=...`** — a committed csproj
property or a command-line value can end up in shell history or a
committed file; an environment variable set from a CI secret does not.
The resolved token is never logged, at any verbosity level.

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
