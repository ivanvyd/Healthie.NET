![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Abstractions

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Abstractions.svg)](https://www.nuget.org/packages/Healthie.NET.Abstractions)

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
| `PulseCheckerState` | Record holding interval, threshold, failure count, history, and last result. |
| `PulseCheckerHealth` | Enum: `Healthy`, `Suspicious`, `Unhealthy`. |
| `PulseInterval` | Enum: 13 intervals from `EverySecond` to `Every5Minutes`. |
| `HealthieOptions` | Global options (e.g. `MaxHistoryLength`). |

## See Also

[Back to main README](../../README.md)
