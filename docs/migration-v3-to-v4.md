# Migrating from v3.x to v4.0

Short guide, because there is little to do. 4.0.0 is a large release — twelve new packages — but it is
source-compatible: **an application that upgrades without touching its code still compiles and still
behaves as it did.** The major number is here for one binary break and one startup behaviour, both
below.

## The one binary break

`HealthieTools`, the read-only MCP tool class in `Healthie.NET.Mcp`, gained a defaulted parameter on
its constructor so it could read the page-size option it had been ignoring:

```csharp
// 3.x
public HealthieTools(IPulsesScheduler pulsesScheduler)

// 4.0
public HealthieTools(IPulsesScheduler pulsesScheduler, HealthieMcpOptions? options = null)
```

Calling code compiles unchanged. An assembly compiled against 3.1.4 and **not rebuilt** will fail to
find the old constructor at runtime. Rebuilding is the whole fix, and almost nobody constructs this
type by hand — it is resolved by the MCP server.

## The relational providers alter your table on startup

`Healthie.NET.Postgres`, `Healthie.NET.SqlServer` and `Healthie.NET.Sqlite` add a `version` column to
their state table the first time they start against a table created before it existed. This is what
optimistic concurrency writes against. The initializer checks whether the column is there before
touching anything, so restarting is safe and repeating it is a no-op.

If your database user cannot `ALTER TABLE`, add the column yourself before deploying — the providers
log what they wanted and carry on without concurrency rather than failing startup.

## Things that changed shape but not behaviour

| Area | What happened | Do you need to act? |
|---|---|---|
| `IStateProvider` | Gained optimistic-concurrency members, all defaulted | No. A provider written against 3.x compiles and reports `SupportsOptimisticConcurrency == false` |
| `IPulseChecker` | Gained `SetScheduleAsync`, defaulted | No, unless you implement the interface directly rather than deriving from `PulseChecker` |
| `IPulseScheduler` | Gained `TryValidateSchedule`, defaulted to accept | No |
| `PulseChecker.SetIntervalAsync` | Now clears any `Schedule` | Only if you relied on setting an interval having no effect on a cron-scheduled checker, which was a bug |
| Dashboard | Opens sectioned by group rather than flat | No. The `GROUP` button still switches to the flat list |

## Worth turning on while you are here

None of these is required, and none changes anything until you add it.

```csharp
builder.Services
    .AddHealthieAlerts()      // health changes become alerts; add a sink to deliver them
    .AddHealthieUptime()      // uptime over real time, not just the rolling history
    .AddHealthieMetrics();    // the library's own meter, read in-process for the dashboard
```

Each one adds a panel or a view to the dashboard on its own — there is no dashboard configuration to
match. See the [dashboard README](https://github.com/ivanvyd/Healthie.NET/blob/main/src/Healthie.Dashboard/README.md).

The full list of what landed is in the [changelog](https://github.com/ivanvyd/Healthie.NET/blob/main/CHANGELOG.md).
