# Contributing to testpulse-dotnet

## Setup

Requires Docker (used to run a pinned .NET 8 SDK, avoiding a host SDK
install — the `net8.0` target framework needs the matching SDK 8.0 image,
not 9.0, which lacks the 8.0 runtime). All `dotnet` commands below assume
a wrapper equivalent to:

```sh
docker run --rm \
  -v "$(pwd):/src" \
  -v testpulse-dotnet-nuget:/root/.nuget/packages \
  -w "/src" \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet "$@"
```

(A local .NET 8 SDK install works too, if you have one.)

## Running tests

```sh
dotnet test tests/TestPulse.MSBuild.Tests
```

## Test-driven development

This project follows red-green-refactor: write a failing test before any
production code, watch it fail for the expected reason, then implement
the minimal code to make it pass.

Two layers of tests exist side by side:

- **Unit tests** (`JUnitReportBuilderTests.cs`, `TestPulseAttachmentsTests.cs`)
  exercise the reflection/XML-building logic and the attachment allowlist
  directly, using a `<ProjectReference>` to the library project. Fast, no
  subprocess spawning.
- **Real end-to-end tests** (`PipelineE2ETests.cs`, `ParallelAttachE2ETests.cs`)
  spawn a real `dotnet test` subprocess (`E2EHelper.RunDotnetTest`)
  against fixture projects under `tests/fixtures/`, which reference the
  library via a real `PackageReference` (not `ProjectReference` — only a
  genuine `PackageReference` triggers NuGet's `build/*.props`/`.targets`
  auto-import mechanism this package relies on for its zero-CLI-flag
  auto-wiring). Submission is verified against a real `HttpListener`-based
  stub server (`StubServer.cs`), not a mock.

Real end-to-end tests are what caught every non-obvious bug during this
package's own development — most notably that `AfterTargets="VSTest"`
never fires when the underlying test run genuinely fails (MSBuild aborts
the rest of the target graph once the built-in `VSTestTask` fails), and
that `$(VSTestLogger)` silently drops one of two combined logger names.
Neither was discoverable from documentation alone.

## Testing the packaging mechanism for real

Because the fixture projects use `PackageReference`, testing a change to
`build/TestPulse.MSBuild.props`/`.targets` requires re-packing and
clearing the NuGet cache for the local `0.0.0-local` version between
iterations (a stale cached package silently masks your edit):

```sh
dotnet pack -o artifacts/local-nuget
rm -rf ~/.nuget/packages/testpulse.msbuild/0.0.0-local
dotnet test tests/TestPulse.MSBuild.Tests
```

`nuget.config` (repo root) points a `local` package source at
`./artifacts/local-nuget`, alongside `nuget.org` for the package's own
real dependencies.

## XML comment gotcha

XML comments (`<!-- -->`) in `.csproj`/`.props`/`.targets` files cannot
contain a literal `--` anywhere in the body — this is an XML spec rule,
not specific to MSBuild, but it's easy to hit while writing explanatory
comments (e.g. "and--this is important"). Not an issue in `.cs` files.

## License

MIT
