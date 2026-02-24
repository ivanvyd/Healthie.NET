![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

**Trust your uptime.** A lightweight, extensible health monitoring framework for .NET applications.

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Abstractions.svg)](https://www.nuget.org/packages/Healthie.NET.Abstractions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Build](https://github.com/ivanvyd/Healthie.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/ivanvyd/Healthie.NET/actions/workflows/ci.yml)

---

## Overview

Healthie.NET lets you define **pulse checkers** -- small classes that monitor the health of databases, APIs, queues, caches, or anything else in your stack. Each checker runs on a configurable interval, tracks consecutive failures with a three-state health model (`Healthy` / `Suspicious` / `Unhealthy`), and persists its state through pluggable providers. Monitor everything through a real-time Blazor dashboard or REST API.

**Why Healthie.NET?**

- **Drop-in and go** -- define a checker, register it, and you're monitoring. No boilerplate.
- **Pluggable architecture** -- swap schedulers and state providers without changing your health check logic.
- **Zero-dependency default scheduler** -- get started immediately with the built-in `PeriodicTimer`-based scheduler.
- **Production-ready CosmosDB persistence** -- persist health check state to Azure CosmosDB for distributed environments.
- **Zero-dependency Blazor dashboard** -- add a real-time monitoring UI to any ASP.NET Core app with two lines of code. No third-party UI libraries required.

---

## Features

- Fully async pulse checkers with `CancellationToken` propagation throughout
- Configurable polling intervals (1 second to 5 minutes via `PulseInterval` enum)
- Three-state health model: `Healthy`, `Suspicious`, `Unhealthy`
- Unhealthy threshold with consecutive failure tracking and automatic state promotion
- Thread-safe concurrent execution prevention via `SemaphoreSlim`
- Real-time state change notifications (`EventHandler<PulseCheckerStateChangedEventArgs>`)
- Assembly scanning for automatic pulse checker discovery and DI registration
- Extensible contracts for custom scheduling (`IPulseScheduler`) and state storage (`IStateProvider`)
- RESTful API controller with configurable routing and authorization
- Zero-dependency Blazor dashboard with event-driven updates, dark/light mode, and per-checker management
- Per-checker lifecycle control: start, stop, trigger, reset, change interval, change threshold
- Comprehensive XML documentation for full IntelliSense support

---

## NuGet Packages

| Package | Description | Install |
|---|---|---|
| **[Healthie.NET.Abstractions](https://www.nuget.org/packages/Healthie.NET.Abstractions)** | Core interfaces, models, enums, and the `PulseChecker` abstract base class. | `dotnet add package Healthie.NET.Abstractions` |
| **[Healthie.NET.DependencyInjection](https://www.nuget.org/packages/Healthie.NET.DependencyInjection)** | DI registration (`AddHealthie`), assembly scanning, and the built-in `TimerPulseScheduler`. | `dotnet add package Healthie.NET.DependencyInjection` |
| **[Healthie.NET.Api](https://www.nuget.org/packages/Healthie.NET.Api)** | ASP.NET Core API controller for managing pulse checkers via REST endpoints. | `dotnet add package Healthie.NET.Api` |
| **[Healthie.NET.CosmosDb](https://www.nuget.org/packages/Healthie.NET.CosmosDb)** | Azure CosmosDB `IStateProvider` implementation for persisting pulse checker state. | `dotnet add package Healthie.NET.CosmosDb` |
| **[Healthie.NET.Dashboard](https://www.nuget.org/packages/Healthie.NET.Dashboard)** | Blazor health monitoring dashboard (Razor Class Library, zero third-party dependencies). | `dotnet add package Healthie.NET.Dashboard` |
| **[Healthie.NET.Scheduling.Quartz](https://www.nuget.org/packages/Healthie.NET.Scheduling.Quartz)** | Quartz.NET `IPulseScheduler` implementation for persistent, CRON-based scheduling. | `dotnet add package Healthie.NET.Scheduling.Quartz` |

---

## Quick Start

### 1. Install Packages

```shell
dotnet add package Healthie.NET.Abstractions
dotnet add package Healthie.NET.DependencyInjection
```

### 2. Create a Pulse Checker

```csharp
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

public class DatabasePulseChecker : PulseChecker
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabasePulseChecker(
        IStateProvider stateProvider,
        IDbConnectionFactory connectionFactory)
        : base(stateProvider, PulseInterval.Every30Seconds, unhealthyThreshold: 3)
    {
        _connectionFactory = connectionFactory;
    }

    public override async Task<PulseCheckerResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = await _connectionFactory
                .CreateConnectionAsync(cancellationToken);
            return new PulseCheckerResult(PulseCheckerHealth.Healthy, "Database connection OK.");
        }
        catch (Exception ex)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"Database connection failed: {ex.Message}");
        }
    }
}
```

### 3. Configure Dependency Injection

```csharp
using Healthie.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHealthie(typeof(Program).Assembly)   // Scan assembly for pulse checkers
    .AddHealthieDefaultScheduler();          // Built-in PeriodicTimer scheduler

var app = builder.Build();
app.Run();
```

### 4. Run

The `PulsesScheduler` hosted service starts automatically on application startup and begins executing all discovered pulse checkers at their configured intervals. No additional configuration is required.

---

## Creating a Pulse Checker

Every pulse checker inherits from the `PulseChecker` abstract base class and overrides `CheckAsync`. The base class handles state management, threshold evaluation, concurrency control, and event notifications.

```csharp
public class ExternalApiPulseChecker : PulseChecker
{
    private readonly HttpClient _httpClient;

    public ExternalApiPulseChecker(
        IStateProvider stateProvider,
        HttpClient httpClient)
        : base(stateProvider, PulseInterval.EveryMinute)
    {
        _httpClient = httpClient;
    }

    public override async Task<PulseCheckerResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            "https://api.example.com/health",
            cancellationToken);

        return response.IsSuccessStatusCode
            ? new PulseCheckerResult(PulseCheckerHealth.Healthy, "API is reachable.")
            : new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"API returned {response.StatusCode}.");
    }
}
```

### Custom Display Name

Override the `DisplayName` property to show a friendly name in the dashboard and API instead of the full class name:

```csharp
public class DatabasePulseChecker : PulseChecker
{
    public DatabasePulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every30Seconds, 3) { }

    public override string DisplayName => "Database Health Check";

    public override async Task<PulseCheckerResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        // ... check logic
    }
}
```

If not overridden, the default `DisplayName` returns the fully-qualified class name (`GetType().FullName`).

### Per-Checker History

Each pulse checker maintains a rolling history of recent executions. History recording can be toggled on/off per checker from the dashboard UI or programmatically:

```csharp
await checker.SetHistoryEnabledAsync(false); // Disable history recording
await checker.ClearHistoryAsync();           // Clear existing history
var history = await checker.GetHistoryAsync(); // Get history entries
```

The maximum number of history entries is configured globally via `HealthieOptions.MaxHistoryLength` (default: 10, range: 1-10).

### Constructor Overloads

| Constructor | Description |
|---|---|
| `PulseChecker(IStateProvider)` | Default interval (`EveryMinute`), threshold `0`. |
| `PulseChecker(IStateProvider, PulseInterval)` | Custom interval, threshold `0`. |
| `PulseChecker(IStateProvider, PulseInterval, uint)` | Custom interval and unhealthy threshold. |

### Health Statuses

| Status | Value | Description |
|---|---|---|
| `Healthy` | `0` | The check passed. Consecutive failure count resets to 0. |
| `Suspicious` | `1` | The check failed, but consecutive failures have not crossed the unhealthy threshold. |
| `Unhealthy` | `2` | The check failed and consecutive failures exceed the threshold. |

### Pulse Intervals

| Interval | Description |
|---|---|
| `EverySecond` | Every 1 second |
| `Every2Seconds` | Every 2 seconds |
| `Every3Seconds` | Every 3 seconds |
| `Every5Seconds` | Every 5 seconds |
| `Every10Seconds` | Every 10 seconds |
| `Every15Seconds` | Every 15 seconds |
| `Every20Seconds` | Every 20 seconds |
| `Every30Seconds` | Every 30 seconds |
| `EveryMinute` | Every 1 minute |
| `Every2Minutes` | Every 2 minutes |
| `Every3Minutes` | Every 3 minutes |
| `Every4Minutes` | Every 4 minutes |
| `Every5Minutes` | Every 5 minutes |

### State Change Events

Subscribe to state transitions on any pulse checker:

```csharp
checker.StateChanged += (sender, args) =>
{
    Console.WriteLine(
        $"Health changed from {args.OldState.LastResult?.Health} " +
        $"to {args.NewState.LastResult?.Health}");
};
```

---

## Configuration

### Basic Setup (Built-in Timer Scheduler)

The simplest configuration uses the built-in `TimerPulseScheduler`, which runs health checks on `PeriodicTimer` intervals with no external dependencies:

```csharp
using Healthie.DependencyInjection;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieDefaultScheduler();
```

### With Quartz.NET Scheduling

For persistent, CRON-based scheduling with clustering support, use the Quartz provider:

```shell
dotnet add package Healthie.NET.Scheduling.Quartz
```

```csharp
using Healthie.Scheduling.Quartz;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieQuartz();
```

You can further configure Quartz with a callback:

```csharp
builder.Services.AddHealthieQuartz(quartz =>
{
    quartz.UsePersistentStore(store =>
    {
        store.UseSqlServer("your-connection-string");
    });
});
```

### With CosmosDB State Persistence

By default, pulse checker state is held in-memory. For durable state persistence across restarts or in distributed environments, use the CosmosDB provider:

```shell
dotnet add package Healthie.NET.CosmosDb
```

```csharp
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;

var cosmosClient = new CosmosClient("your-connection-string");
var container = cosmosClient.GetContainer("your-database", "healthie-state");

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieDefaultScheduler()
    .AddHealthieCosmosDb(container);
```

> **Note:** The CosmosDB container must use `/id` as the partition key path.

### With API Endpoints

```shell
dotnet add package Healthie.NET.Api
```

```csharp
using Healthie.Api;

builder.Services.AddHealthieController();

var app = builder.Build();
app.MapControllers();
app.Run();
```

With authorization:

```csharp
builder.Services.AddHealthieController(
    requireAuthorization: true,
    authorizationPolicy: "AdminPolicy");
```

### Full Example

```csharp
using Healthie.Api;
using Healthie.DependencyInjection;
using Healthie.Scheduling.Quartz;
using Healthie.StateProviding.CosmosDb;
using Healthie.Dashboard;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

var cosmosClient = new CosmosClient("your-connection-string");
var container = cosmosClient.GetContainer("your-database", "healthie-state");

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieQuartz()
    .AddHealthieCosmosDb(container);

builder.Services.AddHealthieController(
    requireAuthorization: true,
    authorizationPolicy: "AdminPolicy");

builder.Services.AddHealthieUI(options =>
{
    options.DashboardTitle = "My App Health";
});

var app = builder.Build();
app.MapControllers();
app.MapHealthieUI().RequireAuthorization("AdminPolicy");
app.Run();
```

---

## API Endpoints

All endpoints are served under the `/healthie` route prefix.

| HTTP Method | Route | Description | Success | Error |
|---|---|---|---|---|
| `GET` | `/` | Get all pulse checker states | `200` with `Dictionary<string, PulseCheckerState>` | `500` |
| `GET` | `/intervals` | Get available polling intervals with descriptions | `200` with `HashSet<PulseIntervalDescription>` | -- |
| `PUT` | `/{checkerName}/interval?interval={value}` | Set the polling interval for a checker | `204` | `400` / `404` |
| `PUT` | `/{checkerName}/threshold?threshold={value}` | Set the unhealthy threshold for a checker | `204` | `400` / `404` |
| `POST` | `/{checkerName}/start` | Start (activate) a checker | `204` | `400` / `404` |
| `POST` | `/{checkerName}/stop` | Stop (deactivate) a checker | `204` | `400` / `404` |
| `POST` | `/{checkerName}/trigger` | Trigger an immediate check execution | `204` | `400` / `404` |
| `PATCH` | `/{checkerName}/reset` | Reset a checker state to healthy | `204` | `400` / `404` |

### Example Requests

```http
GET /healthie

GET /healthie/intervals

PUT /healthie/MyApp.DatabasePulseChecker/interval?interval=Every30Seconds

PUT /healthie/MyApp.DatabasePulseChecker/threshold?threshold=5

POST /healthie/MyApp.DatabasePulseChecker/start

POST /healthie/MyApp.DatabasePulseChecker/stop

POST /healthie/MyApp.DatabasePulseChecker/trigger

PATCH /healthie/MyApp.DatabasePulseChecker/reset
```

> **Note:** The `checkerName` is the fully-qualified type name of the pulse checker class (e.g., `MyApp.DatabasePulseChecker`), which is derived from `GetType().FullName`.

---

## UI Dashboard

The `Healthie.NET.Dashboard` package provides a zero-dependency Blazor monitoring dashboard as a Razor Class Library. No third-party UI frameworks required -- pure HTML/CSS with a professional built-in theme.

**Key features:**

- **Event-driven real-time updates** -- The dashboard subscribes to `IPulseChecker.StateChanged` events and updates individual checker states in-place. No polling, no periodic full re-fetches.
- **Minimalistic row layout** -- Each checker is displayed as a compact inline row showing: status icon, short name, health chip, interval, last run, and trigger button. Click to expand for full details (namespace, failures, error messages, settings, action buttons).
- **Per-checker management** -- Start, stop, trigger, reset, change interval, change threshold -- all from the dashboard.
- **Bulk actions** -- Start All, Stop All, and Trigger All buttons in the toolbar for managing all checkers at once.
- **Summary stat cards** -- Total, healthy, suspicious, and unhealthy counts at a glance with color-coded tints.
- **Dark/light theme** -- Built-in toggle with professional light and dark palettes via CSS custom properties.
- **Search and filter** -- Instantly filter checkers by name.
- **Mobile responsive** -- Clean layout on screens from 375px and up.
- **CSS-only animations** -- Smooth transitions on status changes, hover effects, and fade-in animations.

### Setup

**1. Install the package:**

```shell
dotnet add package Healthie.NET.Dashboard
```

**2. Register services:**

```csharp
using Healthie.Dashboard;

builder.Services.AddHealthieUI(options =>
{
    options.DashboardTitle = "System Health";       // Default: "System Health"
    options.EnableDarkModeToggle = true;            // Default: true
});
```

**3. Map the endpoint (standalone hosting):**

The dashboard is always served at `/healthie/dashboard`.

```csharp
app.MapHealthieUI();
```

**4. (Optional) Require authorization:**

```csharp
app.MapHealthieUI().RequireAuthorization("AdminPolicy");
```

### Dashboard Options

| Option | Type | Default | Description |
|---|---|---|---|
| `DashboardTitle` | `string` | `"System Health"` | Title displayed at the top of the dashboard. |
| `EnableDarkModeToggle` | `bool` | `true` | Whether the dark/light mode toggle is visible. |

### Using the Component Directly

In a Blazor application, you can embed the dashboard component directly in a Razor page instead of using the standalone endpoint:

```razor
@page "/healthie/dashboard"
@using Healthie.Dashboard.Components

<HealthieDashboard />
```

### How Real-Time Updates Work

The dashboard subscribes to `IPulseChecker.StateChanged` events via `SubscribeToStateChanges(Action<string, PulseCheckerState>)`. When a checker's state changes, only that single entry is updated in the dashboard's dictionary -- no polling, no full state re-fetch. A full `GetAllStatesAsync()` is only performed on initial load.

---

## Extensibility

Healthie.NET is designed around extensible contracts. You can create custom providers for both scheduling and state storage without modifying library internals.

### Creating a Custom State Provider

Implement the `IStateProvider` interface to store pulse checker state in your preferred backend:

```csharp
using System.Text.Json;
using Healthie.Abstractions.StateProviding;

public class RedisStateProvider : IStateProvider
{
    private readonly IDatabase _redis;

    public RedisStateProvider(IDatabase redis) => _redis = redis;

    public async Task<TState?> GetStateAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        var value = await _redis.StringGetAsync(name);
        if (value.IsNullOrEmpty) return default;
        return JsonSerializer.Deserialize<TState>(value!);
    }

    public async Task SetStateAsync<TState>(
        string name,
        TState state,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(state);
        await _redis.StringSetAsync(name, json);
    }
}
```

Register it with DI:

```csharp
builder.Services.AddSingleton<IStateProvider, RedisStateProvider>();
```

### Creating a Custom Scheduling Provider

Implement the `IPulseScheduler` interface to control how individual pulse checkers are scheduled and unscheduled:

```csharp
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;

public class HangfirePulseScheduler : IPulseScheduler
{
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default)
    {
        RecurringJob.AddOrUpdate(
            checker.Name,
            () => checker.TriggerAsync(CancellationToken.None),
            interval.ToCronExpression());
        return Task.CompletedTask;
    }

    public Task UnscheduleAsync(
        IPulseChecker checker,
        CancellationToken cancellationToken = default)
    {
        RecurringJob.RemoveIfExists(checker.Name);
        return Task.CompletedTask;
    }
}
```

Register it with DI:

```csharp
builder.Services.AddSingleton<IPulseScheduler, HangfirePulseScheduler>();
```

---

## Sample Projects

The repository includes three sample applications demonstrating different usage patterns:

| Sample | Description | Path |
|---|---|---|
| **Console** | Minimal console application with interactive menu | [`samples/Healthie.Sample.Console`](samples/Healthie.Sample.Console) |
| **WebAPI** | ASP.NET Core Web API with REST endpoints and Swagger | [`samples/Healthie.Sample.WebApi`](samples/Healthie.Sample.WebApi) |
| **BlazorUI** | Blazor Server app with the Healthie.NET UI dashboard | [`samples/Healthie.Sample.BlazorUI`](samples/Healthie.Sample.BlazorUI) |

---

## Migration from v1.x

Healthie.NET v2.0 is a major version with breaking changes. Follow these steps to upgrade.

### Breaking Changes

| Area | v1.x | v2.0 | Action Required |
|---|---|---|---|
| Sync APIs | `IPulseChecker`, `PulseChecker` (sync variants) | Removed | Migrate to async equivalents |
| Async naming | `IAsyncPulseChecker`, `AsyncPulseChecker` | Renamed to `IPulseChecker`, `PulseChecker` | Update type references |
| `Check()` method | `PulseCheckerResult Check()` | `Task<PulseCheckerResult> CheckAsync(CancellationToken)` | Change method signature |
| Scheduling | `AddHealthieQuartz()`, `AddHealthieHangfire()` | `AddHealthieDefaultScheduler()` or `AddHealthieQuartz()` | Replace registration call |
| State providers | `AddHealthieMemoryCache()`, `AddHealthieSqlServer()` | `AddHealthieCosmosDb(container)` | Switch provider |
| API routes | `/sync/*` and `/async/*` prefixes | Fixed at `/healthie/*` | Update client URLs |
| DI scanning | Scans for both sync and async checkers | Scans only for `IPulseChecker` (async) | Remove sync checker classes |
| CancellationToken | Not available | Required parameter (with default) on all async methods | Add parameter if overriding |
| Disposal | `IDisposable` | `IAsyncDisposable` | Update disposal patterns |

### Step-by-Step Migration

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
services.AddHealthie(assemblies).AddHealthieDefaultScheduler();  // Built-in timer
services.AddHealthie(assemblies).AddHealthieQuartz();            // Quartz.NET
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

---

## Releasing New Versions

Releases are automated via GitHub Actions. To publish a new version:

```shell
git tag v2.2.0
git push origin v2.2.0
```

This triggers the CI pipeline which:

1. Builds and packs all NuGet packages with the version from the tag
2. Publishes all packages to [NuGet.org](https://www.nuget.org/)
3. Creates a GitHub Release with auto-generated release notes

For pre-release versions, use a suffix:

```shell
git tag v2.2.0-beta
git push origin v2.2.0-beta
```

Pre-release tags are automatically marked as pre-release on GitHub.

---

## Docker Compatibility

Healthie.NET packages are standard .NET NuGet libraries. They work in Docker containers without any special configuration. When your application runs `dotnet restore` and `dotnet publish` inside a Docker build, Healthie.NET packages are restored and included automatically like any other NuGet dependency. The Blazor dashboard static assets (`_content/Healthie.NET.Dashboard/`) are also included automatically via the Razor Class Library mechanism.

---

## Roadmap

Planned features for future releases:

- **Email/mailing notification support** -- configurable notifications when checkers change state (e.g., healthy to unhealthy)
- **Checker grouping** -- organize pulse checkers into logical groups with group-level actions (trigger all in group, stop all in group, start all in group)
- **Additional state providers** -- SQL Server, PostgreSQL, Redis, and other popular databases
- **Additional scheduling providers** -- Hangfire integration and other scheduling libraries
- **Webhook notifications** -- push state changes to external systems via configurable webhooks

---

## License

This project is licensed under the [MIT License](https://opensource.org/licenses/MIT).
