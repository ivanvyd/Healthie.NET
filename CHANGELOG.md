# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **`Healthie.NET.Mcp`**: a Model Context Protocol server, so an AI agent can read
  and act on service health. `AddHealthieMcp()` + `MapHealthieMcp()` serve it at
  `/healthie/mcp` over Streamable HTTP. Read-only by default; set
  `AllowMutations` to also expose `run_check` and `reset_checker`, and require
  authorization on the endpoint when you do. Tools: `get_health_states`,
  `get_unhealthy_checkers`, `get_checker`, `get_check_history` (paged).
- **`Healthie.NET.AI`**: optional diagnostics that explain what a checker's recent
  history shows. Provider-agnostic through `Microsoft.Extensions.AI`'s
  `IChatClient`, so the host chooses Anthropic, OpenAI, Azure OpenAI, or a local
  model, and this package depends on none of them. Includes a failure-rate anomaly
  comparison that is arithmetic rather than a model call.
- Probe endpoints for orchestrators: `MapHealthieLiveness()` reports that the
  process is up, and `MapHealthieReadiness()` reports 503 while an active checker
  is unhealthy. Liveness deliberately ignores checker state, so a failing
  dependency does not get a working process restarted.
- An Aspire host (`samples/Healthie.AppHost`) that starts the CosmosDB emulator and
  both samples together, and a `docker-compose.yml` with Dockerfiles for anyone
  without the Aspire tooling. Both are development-time only; nothing in `src/`
  depends on either.

- Monitoring of `IHealthCheck` implementations as pulse checkers.
  `AddHealthieForHealthChecks()` adopts every health check already registered with
  `AddHealthChecks()`, and `AddHealthieHealthCheck<T>(name)` adds a single one, so
  health checks written for `Microsoft.Extensions.Diagnostics.HealthChecks` gain
  scheduling, a failure threshold, history, and the dashboard without being
  rewritten. `Degraded` maps to `Suspicious`; note that with the default threshold
  of `0` a degraded check is reported as unhealthy on its first failure, so give it
  a threshold of at least `1` to preserve degraded semantics.
- Support for .NET 10. The shipped libraries now target `net8.0` and `net10.0`, and
  the test suite runs against both. .NET 9 is deliberately skipped: it leaves
  support on the same day as .NET 8.
- `PulseChecker.Name` is now `virtual`, so a checker that wraps something else can
  carry an identity of its own instead of its type name.

- `InMemoryStateProvider`, registered by default. `AddHealthie()` now works
  out of the box without configuring an external state provider such as
  `AddHealthieCosmosDb()`.
- Unit test project (`tests/Healthie.Tests.Unit`, xUnit v3), covering state
  provider semantics and dependency injection registration.
- Central package management via `Directory.Packages.props`.
- `IDisposable` on `PulseChecker` and `TimerPulseScheduler`, alongside the
  existing `IAsyncDisposable`.
- `CosmosDbStateProviderInitializer`, registered by `AddHealthieCosmosDb`, which
  creates the state container on startup if it is missing and fails with a clear
  error when an existing container uses a partition key path other than `/id`.
  This makes the previously unused `IStateProviderInitializer` hosted service do
  something.
- An `AddHealthieCosmosDb(client, databaseId, containerId, throughput)` overload,
  so callers no longer have to build the `Container` themselves. The existing
  `AddHealthieCosmosDb(container)` overload is unchanged.
- CosmosDB documents record the name of the state type they hold, and reading one
  as a different type now throws instead of returning a mismatched state. The type
  was already being written but never checked. The comparison ignores assembly
  version, so state stays readable across releases; documents written before this,
  and those written by releases up to 2.3.0 (which recorded the assembly-qualified
  name), are still readable.
- Interval validation on the API. Hosts that set
  `ApiBehaviorOptions.SuppressModelStateInvalidFilter` do their own model
  validation, so an undefined interval reached the endpoint and failed with a
  500; it is now rejected with a 400. Under the default configuration ASP.NET
  Core already rejected it during model binding.

### Changed

- The samples run without any external dependency. The console sample uses the
  in-memory provider; the Web API and Blazor samples use CosmosDB only when
  `ConnectionStrings:CosmosDb` is configured and fall back to in-memory
  otherwise. Previously all three constructed a `CosmosClient` with an empty
  connection string and threw on startup.
- The API resolves a checker by name and returns 404 when it is absent, rather
  than catching an exception and matching on its message text.

### Removed

- `Newtonsoft.Json` is no longer a dependency of `Healthie.NET.Abstractions`,
  which now depends only on `Microsoft.Extensions.Hosting.Abstractions`. It was
  referenced solely to annotate two enums for the CosmosDB serializer. State
  written by earlier releases still reads back.
- The `System.Text.Json` reference in `Healthie.NET.CosmosDb`, which the framework
  supplies on every supported target.

### Fixed

- A check that failed because this library could not reach its own state store was
  recorded as a failed health check, reporting a healthy component as down. Only
  the monitored component's own failures are recorded now; a state store failure
  surfaces as an error. Cancelling a check no longer records a failure either.
- `AddHealthie()` threw at resolve time when no `IStateProvider` had been
  registered.
- Scheduler and state-provider registration is now order-independent
  (`TryAdd`), so `AddHealthieQuartz()` / `AddHealthieCosmosDb()` win over the
  built-in defaults regardless of call order relative to `AddHealthie()`.
- Scanning the same assembly twice during registration produced duplicate
  checkers, which surfaced as a duplicate-key failure on the states endpoint.
- Assembly scanning aborted host startup on `ReflectionTypeLoadException`;
  checkers that did load are now registered instead of failing startup
  outright.
- Disposing a service provider synchronously threw
  `InvalidOperationException`, because pulse checkers and the timer scheduler
  were only asynchronously disposable. Their disposal is synchronous work, so
  both now implement `IDisposable` as well.
- `StateChanged` compared states by reference for their history list, so it
  fired on every write to a persisted provider and could miss real history
  changes. `PulseCheckerState` now compares by value.
- The state passed as `OldState` on `StateChanged` shared its history list with
  the new state, so it already contained the entry the current run had just
  appended.
- `StartAsync` returned `true` when the checker was already active and `false`
  when it started it, the opposite of `StopAsync` and of the documented
  contract. Both now return `true` only when they changed the state.
- `GetHistoryAsync` returned the stored history list itself, letting callers
  mutate persisted state.
- The dashboard's dark mode toggle could not be enabled: `EnableDarkModeToggle`
  was read-only and always `false`, so the documented
  `options.EnableDarkModeToggle = true` did not compile.
- A state lock that timed out was released anyway, corrupting the semaphore for
  later callers. Timing out now throws instead.
- Project packaging was selected by testing whether a project's path contained
  `src`, which also matched samples and tests when the repository was cloned
  under a path containing that substring.

## [2.3.0] - 2026-02-24

The first release tagged in this repository, published to NuGet as the
`Healthie.NET.*` package set. It carries the async-only architecture, the
Blazor dashboard, and the CosmosDB and Quartz.NET providers.

### Added

- Centralized build configuration (`Directory.Build.props`) covering shared
  package metadata, SourceLink, and symbol packages.
- Automated CI and tag-driven NuGet publishing via GitHub Actions.

### Changed

- Dashboard UI improvements and additional sample pulse checkers.

[Unreleased]: https://github.com/ivanvyd/Healthie.NET/compare/v2.3.0...HEAD
[2.3.0]: https://github.com/ivanvyd/Healthie.NET/releases/tag/v2.3.0
