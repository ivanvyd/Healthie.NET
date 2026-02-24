![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Quartz

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Quartz.svg)](https://www.nuget.org/packages/Healthie.NET.Quartz)

Quartz.NET `IPulseScheduler` implementation for Healthie.NET. Provides persistent, CRON-based scheduling with support for clustering and advanced job store configurations.

## Installation

```shell
dotnet add package Healthie.NET.Quartz
```

## Usage

Basic setup with default Quartz configuration:

```csharp
using Healthie.Scheduling.Quartz;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieQuartz();
```

With custom Quartz configuration:

```csharp
builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieQuartz(quartz =>
    {
        quartz.UsePersistentStore(store =>
        {
            store.UseSqlServer("your-connection-string");
        });
    });
```

## Key Types

| Type | Description |
|---|---|
| `StartupExtensions.AddHealthieQuartz()` | Registers Quartz.NET services and `QuartzPulseScheduler` as `IPulseScheduler`. |
| `QuartzPulseScheduler` | Implements `IPulseScheduler` using Quartz jobs with CRON triggers. |
| `PulseCheckerJob` | Internal Quartz `IJob` that resolves and triggers a pulse checker by name. |

## How It Works

- Each pulse checker is scheduled as a Quartz job identified by its fully-qualified type name.
- `PulseInterval` values are converted to CRON expressions via `PulseIntervalExtensions.ToCronExpression()`.
- The `PulseCheckerJob` resolves the checker from DI by name, avoiding serialization of complex objects.
- Rescheduling a checker removes the existing job before creating a new one.

## When to Use

Use this instead of the built-in `TimerPulseScheduler` when you need:
- Persistent job storage across restarts
- CRON-based scheduling
- Quartz clustering for distributed environments

## See Also

[Back to main README](../../README.md)
