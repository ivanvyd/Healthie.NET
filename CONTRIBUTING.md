# Contributing to Healthie.NET

Thanks for your interest in improving Healthie.NET. This document covers what
you need to build the project, the repo layout, and what we look for in a
pull request.

## Building and Testing

```shell
dotnet build Healthie.NET.sln          # build all projects; NuGet packages are generated on build
dotnet test                            # run the unit test suite
```

Running the samples (`samples/Healthie.Sample.Console`,
`samples/Healthie.Sample.WebApi`, `samples/Healthie.Sample.BlazorUI`) requires
a reachable CosmosDB instance (the [CosmosDB emulator](https://learn.microsoft.com/azure/cosmos-db/how-to-develop-emulator)
works fine) — see each sample's `Program.cs` for its connection string
placeholder.

## Repository Layout

| Path | Contents |
|---|---|
| `src/` | The packages that ship to NuGet. Everything here is public API surface. |
| `samples/` | Consumer applications demonstrating usage. Not packed or published. |
| `tests/` | Test projects. `tests/Healthie.Tests.Unit` is the unit test suite (xUnit). |
| `fabric/standards/` | The project's coding standards — read the relevant file before a larger change. |

## Coding Standards

Follow the standards in `fabric/standards/` (global, backend, library-design,
testing). A few load-bearing conventions, also enforced by `.editorconfig`:

- Async-only public API: no sync method variants, every method takes a
  defaulted `CancellationToken`, disposal is `IAsyncDisposable`.
- `ConfigureAwait(false)` on every await in library code — except in Blazor
  components (`src/Healthie.Dashboard`), where it breaks the sync context
  `StateHasChanged()` needs.
- C# 12 idiom: primary constructors, file-scoped namespaces, collection
  expressions, records for immutable data.
- XML docs are required on all public API — `GenerateDocumentationFile` is
  enabled, so missing docs on new public members will surface as build
  warnings.

## Central Package Management

This repo uses [central package management](https://learn.microsoft.com/nuget/consume-packages/central-package-management).
Package versions live in `Directory.Packages.props`, not in individual
`.csproj` files. When adding or bumping a dependency:

- Add/update the `<PackageVersion>` entry in `Directory.Packages.props`.
- Reference the package from the `.csproj` with `<PackageReference Include="..." />`
  and **no** `Version=` attribute — an inline version will conflict with
  central package management and fail the restore.

## Submitting a Pull Request

- Keep PRs small and focused on one change. Large, mixed-purpose PRs are
  harder to review and more likely to stall.
- Add or update tests in `tests/Healthie.Tests.Unit` for any behavior change.
  PRs that change behavior without test coverage will be asked for tests.
- Add XML docs for any new public type or member.
- Don't bump package versions yourself — all packages share one version, and it
  comes from the git tag at release time (see [Releasing](#releasing) below).
  Do add an entry under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md)
  describing the change; that entry is what the release notes are built from.
- Run `dotnet build` and `dotnet test` locally before opening the PR; CI runs
  both on every push and pull request.

## Releasing

Maintainers only.

### Where the version lives

**The git tag is the only source of truth.** No version is committed anywhere —
there is no `<Version>` in `Directory.Build.props`, by design, so nothing can
drift out of sync with what was actually shipped. The release workflow passes
`-p:Version=` from the tag, and every package shares that one number.

A consequence worth knowing: `dotnet pack` on your machine produces `1.0.0`
packages, because no version was passed. That is expected and is never what gets
published.

### Working out the next number

Three commands tell you everything:

```shell
gh release list --limit 5                  # what has been released
git tag -l --sort=-v:refname | head        # the tags behind those releases
sed -n '/## \[Unreleased\]/,/## \[/p' CHANGELOG.md   # what is going out next
```

The highest tag is the current version; pick the next one from what the
`[Unreleased]` section of [CHANGELOG.md](CHANGELOG.md) contains, per
[SemVer](https://semver.org):

| `[Unreleased]` contains | Next version |
|---|---|
| Anything under `### Removed`, or a documented behavior change that can break a caller | **major** — `2.3.0` → `3.0.0` |
| New API or packages under `### Added`, all backward compatible | **minor** — `2.3.0` → `2.4.0` |
| Only `### Fixed` | **patch** — `2.3.0` → `2.3.1` |

Shipping a major in steps? Use a pre-release suffix — `3.0.0-preview.1`, then
`3.0.0-rc.1`, then `3.0.0`. NuGet and GitHub both treat any `-suffix` version as
a pre-release, so it will not be served to `dotnet add package` by default.

Two rules that are not negotiable, because NuGet enforces them:

- **Versions only ever go up.** `2.4.0` cannot follow `3.0.0`.
- **A published version can never be reused**, even if you unlist it. If you
  publish a bad `3.0.0`, the fix is `3.0.1` — never a re-push of `3.0.0`.

The workflow refuses to release a version whose tag already exists, so a
mistyped number fails before anything is built rather than after it is public.

### Running the release

```shell
gh workflow run Release -f version=3.0.0 -f dry_run=true    # rehearse
gh run watch
gh workflow run Release -f version=3.0.0 -f dry_run=false   # publish
```

Pass the version without a leading `v`; the workflow adds it when it tags. A dry
run builds, tests, and packs, and uploads the `.nupkg` files as a run artifact
without tagging or publishing anything — do that first and download the artifact
if you want to inspect what would ship.

A real run tags the commit, pushes every package plus symbols to NuGet.org, and
creates a GitHub release. Tests gate the pack in both modes, so a release cannot
publish a build that does not pass.

Pushing a `v*` tag by hand triggers the same workflow, which is the older path
and still supported:

```shell
git tag v3.0.0 && git push origin v3.0.0
```

Before releasing, move the `[Unreleased]` entries in `CHANGELOG.md` under a new
`## [x.y.z] - YYYY-MM-DD` heading.

## Reporting Bugs and Requesting Features

Use the issue templates when opening a new issue — they collect the details
(package/version, .NET version, scheduler and state provider in use) needed to
reproduce most problems quickly. Please report suspected security
vulnerabilities privately per [SECURITY.md](SECURITY.md) instead of opening a
public issue.
