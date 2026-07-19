![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Abstractions

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Abstractions.svg)](https://www.nuget.org/packages/Healthie.NET.Abstractions)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Core interfaces, models, enums, and the `PulseChecker` abstract base class for the Healthie.NET health monitoring framework. This package contains no implementations -- only the contracts and types that all other Healthie.NET packages depend on.

## Installation

```shell
dotnet add package Healthie.NET.Abstractions
```

## Usage

Inherit from `PulseChecker` and override `CheckAsync` to implement a health check:

```csharp
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

public class DatabasePulseChecker : PulseChecker
{
    private readonly IDbConnectionFactory _db;

    public DatabasePulseChecker(IStateProvider stateProvider, IDbConnectionFactory db)
        : base(stateProvider, PulseInterval.Every30Seconds, unhealthyThreshold: 3)
    {
        _db = db;
    }

    public override async Task<PulseCheckerResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var conn = await _db.CreateConnectionAsync(cancellationToken);
        return new PulseCheckerResult(PulseCheckerHealth.Healthy, "OK");
    }
}
```

## Key Types

| Type | Description |
|---|---|
| `PulseChecker` | Abstract base class -- inherit to create a health check. |
| `IPulseChecker` | Full pulse checker contract (`IPulse + IState + IAsyncDisposable`). |
| `IStateProvider` | Contract for state persistence (`GetStateAsync` / `SetStateAsync`). |
| `IPulseScheduler` | Contract for scheduling (`ScheduleAsync` / `UnscheduleAsync`). |
| `PulseCheckerResult` | Record returned from `CheckAsync` with `Health` and `Message`. |
| `PulseCheckerState` | Record holding interval, threshold, failure count, history, tags, group, pin, and last result. |
| `PulseCheckerHealth` | Enum: `Healthy`, `Suspicious`, `Unhealthy`. |
| `PulseInterval` | Enum: 13 intervals from `EverySecond` to `Every5Minutes`. |
| `HealthieOptions` | Global options (e.g. `MaxHistoryLength`). |

## Describing a checker

A checker can say where it belongs and what it is, and both are picked up by the dashboard.

```csharp
public class RedisCachePulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override string DisplayName => "Redis Cache";

    // One group at most: this is where the checker lives.
    public override string DefaultGroup => "Data Stores";

    // Any number of tags: these describe it, and may cut across groups.
    public override IReadOnlyList<string> DefaultTags => ["tier-1", "cache"];

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => /* ... */;
}
```

Both seed `PulseCheckerState` the first time a checker runs and are never applied again, so a
change made later -- through `SetGroupAsync` / `SetTagsAsync`, or on the dashboard -- survives a
restart rather than being reset to what the code says.

`SetPinnedAsync` sorts a checker above the rest. A pin is stored with the checker rather than per
viewer, on the grounds that the checks worth watching are the same for everyone.

## See Also

[Back to main README](../../README.md)
