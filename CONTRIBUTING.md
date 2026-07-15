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
- Don't bump package versions or add release notes yourself — all packages
  share one version driven by the `v*` git tag at release time
  (see [Releasing](README.md#releasing-new-versions) in the README).
- Run `dotnet build` and `dotnet test` locally before opening the PR; CI runs
  both on every push and pull request.

## Reporting Bugs and Requesting Features

Use the issue templates when opening a new issue — they collect the details
(package/version, .NET version, scheduler and state provider in use) needed to
reproduce most problems quickly. Please report suspected security
vulnerabilities privately per [SECURITY.md](SECURITY.md) instead of opening a
public issue.
