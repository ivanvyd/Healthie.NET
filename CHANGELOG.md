# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

Eleven new packages, and the schedule model that several of them needed. Everything here is
additive: no public API was removed or changed, and an application that upgrades without touching
its code behaves exactly as it did.

### Added

- **Optimistic concurrency on `IStateProvider`.** A state write can now be made conditional on the
  state not having changed since it was read, closing the lost update that has been documented on
  `CosmosDbStateProvider` since the beginning: a setting changed from the dashboard could be
  overwritten by a check that had read the state first. `GetStateEntryAsync` returns the state with
  the version it was read at, and `TrySetStateAsync` writes only if that version is still current,
  reporting a refused write rather than throwing -- under contention a conflict is expected, not
  exceptional. `UpdateStateAsync` is the read-modify-write retry loop over the pair, and every
  setting on `PulseChecker` goes through it -- as does a check storing its own result, which reads
  and writes the whole state and so used to put every other field back to what it was when the
  check started. Within one process a semaphore hid that; across two replicas sharing one store
  nothing did, and that is the direction the dashboard actually loses to.

  This was expected to need a major release and did not. The new members are defaulted interface
  methods: a provider written against the old two-method interface still compiles, still works, and
  reports `SupportsOptimisticConcurrency == false` rather than pretending. CosmosDB uses ETags,
  the relational providers a version column added to existing tables on startup, and the in-memory
  provider a compare-and-swap.
- **`HealthChanged` on `PulseCheckerStateChangedEventArgs`**, with `PreviousHealth` and
  `CurrentHealth` beside it. `StateChanged` fires on every check -- a stored result always moves the
  execution time -- which was listed as a known issue and is not one: the dashboard redraws from
  exactly those events. What was missing was a way to ask the narrower question, so every handler
  that only cared about a component going down reached into `OldState`/`NewState.LastResult?.Health`
  and worked it out again. The alerting and uptime packages were doing that identically; both now
  read `PreviousHealth` and `CurrentHealth` from the event instead of digging for them.
- **Schedules.** `PulseSchedule` says either "every this long" or "on this cron expression", and
  sits alongside `PulseInterval` rather than replacing it. The enum stopped at five minutes, which
  is short of what a certificate-expiry or disk-space check wants. Cron is standard Unix syntax and
  is evaluated in UTC by every scheduler.
- **`Healthie.NET.Postgres`, `Healthie.NET.SqlServer`, `Healthie.NET.Sqlite`** and the
  `Healthie.NET.Relational` engine behind them, which works against any database with an ADO.NET
  driver. PostgreSQL also covers Databricks Lakebase, which is managed PostgreSQL.
- **`Healthie.NET.Hangfire`.** Schedules live in Hangfire's storage, so they survive a restart and
  each occurrence runs on exactly one replica.
- **`Healthie.NET.Coravel`**, for applications already running Coravel. Coravel has no API for
  removing a scheduled job, so Healthie owns the due times and Coravel supplies the tick; the
  package README says so, and says to use the built-in timer if Coravel is not already there.
- **`Healthie.NET.Temporal`.** Schedules live in the Temporal cluster. It needs a cluster and the
  SDK carries a native core, both of which the README weighs against Hangfire and the built-in timer.
- **`Healthie.NET.Checkers`.** HTTP endpoints, TCP ports, TLS certificate expiry, DNS resolution and
  disk space, with no checker code to write. Certificate expiry and disk space report *suspicious*
  before they report unhealthy, because both are gradual and the warning is the useful signal.
- **`Healthie.NET.Alerting`.** Health changes become alerts, delivered to a webhook or your own
  `IAlertSink`, with transition-only firing, recovery notifications and flap suppression. A sink that
  throws, hangs or backs up cannot delay a check, hold its semaphore, or mark anything unhealthy.
- **`Healthie.NET.Uptime`.** Records health transitions rather than checks, so uptime is exact over
  any window and small enough to keep for a year -- the rolling history holds a hundred results,
  which for a one-second checker is a hundred seconds. Time the application was not running is
  reported as unknown rather than counted as either up or down.
- **`Healthie.NET.LeaderElection`.** Runs the checks on one replica at a time. Without it, three
  replicas ask a database three times whether it is healthy and race to write the same state.
- **Metrics and traces.** A `Meter` and an `ActivitySource` named `Healthie.NET`, discovered by
  OpenTelemetry by name with no package to install: check duration, results by health, health
  transitions, and triggers skipped because the previous check had not finished. That last one is
  the only place a checker outlasting its own interval shows up.
- **Bulk state reads.** `IStateProvider.GetStatesAsync` reads every checker in one call, which is
  what a dashboard load and a list request do. Defaulted to the old one-read-per-name behaviour, so
  existing providers are unaffected.
- **State removal.** `IStateProvider.DeleteStateAsync` cleans up after a checker that was renamed or
  removed. Defaulted to refuse rather than to silently do nothing.

### Fixed

- A cron schedule whose next occurrence was more than about fifty days out **stopped the checker
  permanently and silently**. `Task.Delay` refuses a longer wait and throws, and that throw is not a
  cancellation, so the scheduler's loop ended with nothing logged. An annual certificate check
  reached it. Long waits are now taken in bounded steps.
- Quartz and the built-in scheduler read the same cron expression in **different timezones** --
  Quartz defaults to the machine's local zone, the built-in scheduler evaluates in UTC. Both are
  pinned to UTC now, matching what the dashboard renders.
- Quartz quietly accepted out-of-range cron fields: `99 99 * * *` produced a trigger firing every
  minute, for ever. Ranges are checked before Quartz sees them.
- Cron expressions were compared as typed, so `0 0 * * MON-FRI` and `0  0  *  *  mon-fri` looked
  like a change to the checker. They are normalized, as tags already were.

### Security

- CodeQL, OpenSSF Scorecard and dependency review run on every pull request, releases carry a
  CycloneDX SBOM, every GitHub Action is pinned to a commit SHA, and every workflow drops to
  deny-by-default permissions.
- Four high-severity advisories resolved: `SQLitePCLRaw.lib.e_sqlite3` (GHSA-2m69-gcr7-jv3q),
  `Newtonsoft.Json` reached through Hangfire (GHSA-5crp-9r3c-p9vr), and `System.Text.Json` in the
  console sample (CVE-2024-30105 and CVE-2024-43485). The solution reports no vulnerable packages.

## [3.1.4] - 2026-07-19

### Changed

- Each shipped package's README now links the live demo -- `healthie.compiletheory.com`, a read-only
  hosted instance of the dashboard -- so the link appears on the package's listing on nuget.org.

## [3.1.3] - 2026-07-18

### Changed

- The dashboard now scrolls within its own board rather than the page, so its scrollbar is themed to
  match the board -- a thin, rounded bar in the board's own line colour, on both the dark and light
  themes -- instead of the platform's wide light-grey default running down the side of a dark tool.
  The board owns a full-viewport (`100dvh`) scroll region, and a scroll that reaches its end no
  longer drags the page behind an embedded board.

## [3.1.2] - 2026-07-18

### Fixed

- The prerendered dashboard no longer risks dropping its own circuit on a board with much state.
  3.1.1 carried the states from prerender to the interactive render by persisting them into the
  page, which put the whole board -- every checker and all its history -- on the SignalR circuit.
  That payload grows with each checker, and past SignalR's default 32&#160;KB message limit the hub
  rejects it and the circuit never starts: the board freezes on the prerendered snapshot, styled but
  not live. The states now stay on the server between the two renders (Blazor Server holds them there
  already) and only a short-lived token travels to the browser and back, so the wire cost is constant
  whatever the size of the board. A token that no longer resolves falls back to a fresh read. No
  SignalR limit needs raising, so no extra memory or denial-of-service exposure comes with it.

## [3.1.1] - 2026-07-18

### Fixed

- The dashboard no longer flickers on load when it is prerendered. Hosted the usual
  way -- `<HealthieDashboard />` on an interactive-server page, which prerenders by
  default -- the server rendered the full board, then the circuit connected a moment
  later and the component started again from an empty state, read every checker afresh,
  and briefly replaced the board already on screen. In that gap a status icon, which the
  dashboard stylesheet sizes against its surroundings, painted for a frame before those
  rules applied and filled the board as one large ring. The states the prerender reads
  are now carried across to the interactive render through `PersistentComponentState`,
  so the first interactive render matches the prerendered one: nothing is replaced, no
  frame shows an unsized icon, and the redundant second read of every checker is gone.

## [3.1.0] - 2026-07-18

### Added

- `HealthieUIOptions.AllowMutations`, which decides whether the dashboard renders the
  controls that change a checker. It defaults to `true`, so an existing dashboard is
  unchanged. Setting it to `false` leaves a board that only reports: every state,
  sparkline, group, tag, and event stays, and nothing that would run, pause, reset,
  retime, or retag a checker is rendered -- which is what makes the dashboard safe to
  show to an audience wider than the people trusted to stop a production check. Nothing
  is lost to the reader, because the values behind the editors are already on the board:
  the interval is the row's rate, the threshold is the denominator in `FAILS`, and the
  group and tags are the chips under each name.

  It is one setting for the whole application and it is not authorization -- it decides
  what the board can do, never who is looking. It composes with `RequireAuthorization`
  on the endpoint, which decides the latter.

  This closes a gap against the project's own read-only-by-default principle, which
  `Healthie.NET.Mcp` already honoured through `HealthieMcpOptions.AllowMutations`.
  `Healthie.NET.Api` still exposes its six mutating endpoints ungated, and is left for
  its own change.

### Changed

- Package tags now lead with the terms people search for -- `watchdog`, `uptime`,
  `healthchecks`, `observability` -- rather than `pulse`, which is this library's own
  vocabulary. They are declared once in `Directory.Build.props` and appended to per
  project, which is also how `healthchecks` came to be missing from six of the eight:
  the list was written out by hand each time and drifted.

### Fixed

- The release workflow no longer pushes symbol packages twice. `dotnet nuget push`
  already uploads each `.snupkg` next to its `.nupkg`, so the separate step only
  re-sent what had just been sent, and survived the resulting eight `Conflict ...
  already exists` errors by carrying `continue-on-error`.

## [3.0.0] - 2026-07-15

### Added

- Groups and tags on a checker, each answering a different question. A checker belongs
  to at most one **group** (`PulseChecker.DefaultGroup`, `IPulseChecker.SetGroupAsync`),
  which is what the dashboard's grouped view sections by -- one group each is what makes
  that view a partition, where every checker appears exactly once and a group's tallies
  add up. It may carry any number of **tags** (`PulseChecker.DefaultTags`,
  `IPulseChecker.SetTagsAsync`), which describe it and filter the list, and are free to
  cut across groups. Both are declared in code as defaults and can be changed from the
  dashboard afterwards; both are stored in `PulseCheckerState`, so an edit outlives a
  restart and the defaults only ever seed a checker that has no stored state yet.
- Pinning a checker (`PulseChecker.SetPinnedAsync`), which sorts it above the rest.
  A pin is stored rather than held per-viewer: the checks worth watching are the same
  for everyone.
- The dashboard can lay the checkers out as rows or as cards, and can section them by
  group, with each section collapsible and reporting its own healthy/suspicious/failing
  tallies.
- A legend and about panel on the dashboard, behind the `?` in the header, explaining
  what the colours, the pulse strip, and the controls mean, and linking to the
  repository, the author, and the license. Every control also explains itself on hover.

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

- Every time the dashboard shows is now UTC, and says so. It renders on the server, so
  the local time it used to print was the server's, shown to a viewer who may be nowhere
  near it -- a clock that was right only for the host. UTC reads the same for everyone.
- The dashboard is redesigned as a pulse monitor: an aggregate EKG trace, one row
  per checker with a per-run pulse strip, a side panel for the selected checker,
  and a live event log of state transitions. It stays a zero-dependency Blazor
  component library with no web fonts, so it runs air-gapped. Dark mode is now the
  default.
- `HealthieOptions.MaxHistoryLength` accepts up to 100 entries, up from 10, so the
  dashboard's pulse strip can show a longer window. The default is still 10, and
  out-of-range values are still clamped.
- Each package's NuGet listing now shows that package's own README. Every package
  packed the repository README, so all eight advertised the whole framework and
  linked to files that only exist in the repo. `Healthie.NET.Mcp` and
  `Healthie.NET.AI` have gained a README of their own.

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

- The dashboard raised `StateHasChanged` from whichever thread ran the check, which
  Blazor rejects, so every state change threw and was swallowed by the subscriber's error
  handling -- roughly one logged exception and stack trace per check, forever, in the
  host's own logs. The renders were only appearing because the once-a-second clock tick
  redrew the component and picked up the change on its way past, which meant the
  dashboard was quietly polling rather than being event-driven as documented. The work is
  now marshalled onto the renderer's dispatcher.
- The aggregate pulse trace jumped once per cycle at every window width but one. Its tile
  was measured in SVG user units while the scroll that loops it moved a fixed number of
  CSS pixels, and the two only agreed when the window happened to be exactly 1760px wide.
  The same mismatch squashed the trace horizontally on a narrow window.
- Icons sat a few pixels below the boxes they were given, because an `<svg>` is an inline
  element and was aligning to the text baseline.
- The page served by `MapHealthieUI()` left the browser's default body margin in place,
  which drew a pale frame around the dashboard.
- The dashboard fetched each checker's history separately even though a checker's
  state already carries it, costing one extra provider round-trip per checker on
  load and another on every state change. Against a remote provider this dominated
  load time; with 14 checkers on Azure CosmosDB the dashboard took ~3.5s to render
  and now takes ~0.4s.

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

[Unreleased]: https://github.com/ivanvyd/Healthie.NET/compare/v3.1.4...HEAD
[3.0.0]: https://github.com/ivanvyd/Healthie.NET/compare/v2.3.0...v3.0.0
[2.3.0]: https://github.com/ivanvyd/Healthie.NET/releases/tag/v2.3.0
