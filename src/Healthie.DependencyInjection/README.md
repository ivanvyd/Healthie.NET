![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.DependencyInjection

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.DependencyInjection.svg)](https://www.nuget.org/packages/Healthie.NET.DependencyInjection)

DI registration and the built-in `TimerPulseScheduler` for Healthie.NET. This package scans assemblies for `PulseChecker` implementations, registers them as singletons, and provides a zero-dependency `PeriodicTimer`-based scheduler.

## Installation

```shell
dotnet add package Healthie.NET.DependencyInjection
```

## Usage

```csharp
using Healthie.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHealthie(typeof(Program).Assembly)   // Scan for PulseChecker implementations
    .AddHealthieDefaultScheduler();          // Built-in PeriodicTimer scheduler

var app = builder.Build();
app.Run();
```

With custom options:

```csharp
builder.Services.AddHealthie(
    options => options.MaxHistoryLength = 10,
    typeof(Program).Assembly);
```

## Key Types

| Type | Description |
|---|---|
| `StartupExtensions.AddHealthie()` | Registers core services and scans assemblies for pulse checkers. |
| `TimerPulseScheduler` | Built-in `IPulseScheduler` using `PeriodicTimer`. Zero external dependencies. |

## What It Registers

- All discovered `IPulseChecker` implementations as singletons
- `IPulsesScheduler` / `PulsesScheduler` as a hosted service
- `IPulseScheduler` / `TimerPulseScheduler` as the default scheduler
- `HealthieOptions` as a singleton
- `StateProviderInitializationService` as a hosted service

## See Also

[Back to main README](../../README.md)
