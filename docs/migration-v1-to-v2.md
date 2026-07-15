# Migrating from v1.x to v2.0

This guide covers the breaking changes introduced in v2.0. If you are starting fresh, you do not
need it -- see the [README](../README.md) instead.

## Breaking Changes

| Area | v1.x | v2.0 | Action Required |
|---|---|---|---|
| Sync APIs | `IPulseChecker`, `PulseChecker` (sync variants) | Removed | Migrate to async equivalents |
| Async naming | `IAsyncPulseChecker`, `AsyncPulseChecker` | Renamed to `IPulseChecker`, `PulseChecker` | Update type references |
| `Check()` method | `PulseCheckerResult Check()` | `Task<PulseCheckerResult> CheckAsync(CancellationToken)` | Change method signature |
| Scheduling | `AddHealthieQuartz()`, `AddHealthieHangfire()` | `AddHealthie()` registers the built-in timer scheduler; call `AddHealthieQuartz()` to override it | Replace registration call |
| State providers | `AddHealthieMemoryCache()`, `AddHealthieSqlServer()` | `AddHealthieCosmosDb(container)` | Switch provider |
| API routes | `/sync/*` and `/async/*` prefixes | Fixed at `/healthie/*` | Update client URLs |
| DI scanning | Scans for both sync and async checkers | Scans only for `IPulseChecker` (async) | Remove sync checker classes |
| CancellationToken | Not available | Required parameter (with default) on all async methods | Add parameter if overriding |
| Disposal | `IDisposable` | `IAsyncDisposable` | Update disposal patterns |

## Step-by-Step Migration

**Step 1:** Update NuGet packages to v2.0.0-beta.

**Step 2:** Replace sync pulse checkers. Change the base class and override `CheckAsync` instead of `Check`:

```csharp
// v1.x
public class MyChecker : PulseChecker
{
    public MyChecker(IStateProvider provider) : base(provider) { }
    public override PulseCheckerResult Check()
        => new(PulseCheckerHealth.Healthy);
}

// v2.0
public class MyChecker : PulseChecker
{
    public MyChecker(IStateProvider provider) : base(provider) { }
    public override Task<PulseCheckerResult> CheckAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy));
}
```

**Step 3:** Replace scheduling registration:

```csharp
// v1.x
services.AddHealthie(assemblies).AddHealthieQuartz();

// v2.0 -- choose one:
services.AddHealthie(assemblies);                     // Built-in timer (registered by default)
services.AddHealthie(assemblies).AddHealthieQuartz(); // Quartz.NET
```

**Step 4:** Replace state provider registration:

```csharp
// v1.x
services.AddHealthieMemoryCache();
// or
services.AddHealthieSqlServer(connectionString);

// v2.0
services.AddHealthieCosmosDb(cosmosContainer);
```

**Step 5:** Update API client URLs. Remove `/sync/` and `/async/` prefixes:

```
// v1.x
GET  /healthie/async
PUT  /healthie/async/{name}/interval

// v2.0
GET  /healthie
PUT  /healthie/{name}/interval
```

**Step 6 (optional):** Add the new UI dashboard:

```csharp
services.AddHealthieUI();
// ...
app.MapHealthieUI();  // Serves at /healthie/dashboard
```
