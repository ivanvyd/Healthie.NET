![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

**Trust your uptime.** A lightweight, extensible health monitoring framework for .NET applications.

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Abstractions.svg)](https://www.nuget.org/packages/Healthie.NET.Abstractions)
[![Downloads](https://img.shields.io/nuget/dt/Healthie.NET.Abstractions.svg)](https://www.nuget.org/packages/Healthie.NET.Abstractions)
[![Build](https://github.com/ivanvyd/Healthie.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/ivanvyd/Healthie.NET/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4.svg)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/ivanvyd/Healthie.NET/blob/main/LICENSE)

```csharp
// Finds every pulse checker in your app and starts monitoring on the intervals they declare.
// No scheduler to configure, no storage to stand up.
builder.Services.AddHealthie(typeof(Program).Assembly);
```

**Contents**

[Quick Start](#quick-start) · [Packages](#nuget-packages) · [Writing a Checker](#creating-a-pulse-checker) · [Configuration](#configuration) · [REST API](#api-endpoints) · [Dashboard](#ui-dashboard) · [Existing `IHealthCheck`s](#monitoring-existing-health-checks) · [Kubernetes](#kubernetes-probes) · [MCP](#ai-agents-mcp) · [AI Diagnostics](#ai-diagnostics) · [Extensibility](#extensibility) · [Samples](#sample-projects) · [Roadmap](#roadmap) · [Contributing](#contributing)

---

## Overview

Healthie.NET lets you define **pulse checkers** -- small classes that monitor a database, an API, a queue, a cache, or anything else in your stack. Each runs on its own interval, and rather than reporting only pass or fail, it counts consecutive failures against a threshold across three states (`Healthy` / `Suspicious` / `Unhealthy`). That is the difference between a blip and an outage, and it is what makes a health signal worth alerting on.

Watch it all through a live dashboard, a REST API, Kubernetes probes, or an AI agent.

**What sets it apart**

- **It doesn't replace your health checks -- it schedules them.** Already have `IHealthCheck` implementations, yours or the community ones for SQL Server, Redis, RabbitMQ, Azure, or AWS? [One call](#monitoring-existing-health-checks) gives every one of them intervals, thresholds, and history, with nothing rewritten.
- **Nothing to stand up.** `AddHealthie()` registers a `PeriodicTimer` scheduler and an in-memory store, so it runs on its own. Swap in Quartz or CosmosDB when you outgrow them -- your checker code doesn't change.
- **Current.** .NET 8 and .NET 10, async throughout, `CancellationToken` everywhere.
- **A dashboard with no dependencies.** Pure HTML and CSS, no UI framework, no web fonts -- two lines to add, and it runs air-gapped.
- **Built for agents.** An [MCP server](#ai-agents-mcp) that is read-only until you say otherwise, plus [optional diagnostics](#ai-diagnostics) through any `IChatClient`.

Everything heavy is opt-in: `Healthie.NET.Abstractions` carries exactly one dependency, and Quartz, CosmosDB, MCP, AI, and the dashboard are separate packages you add only if you want them.

---

## NuGet Packages

| Package | Description | Install |
|---|---|---|
| **[Healthie.NET.Abstractions](https://www.nuget.org/packages/Healthie.NET.Abstractions)** | Core interfaces, models, enums, and the `PulseChecker` abstract base class. | `dotnet add package Healthie.NET.Abstractions` |
| **[Healthie.NET.DependencyInjection](https://www.nuget.org/packages/Healthie.NET.DependencyInjection)** | DI registration (`AddHealthie`), assembly scanning, the built-in `TimerPulseScheduler`, the in-memory state provider, and the `IHealthCheck` bridge. | `dotnet add package Healthie.NET.DependencyInjection` |
| **[Healthie.NET.Api](https://www.nuget.org/packages/Healthie.NET.Api)** | ASP.NET Core API controller for managing pulse checkers via REST, plus liveness and readiness probe endpoints. | `dotnet add package Healthie.NET.Api` |
| **[Healthie.NET.CosmosDb](https://www.nuget.org/packages/Healthie.NET.CosmosDb)** | Azure CosmosDB `IStateProvider` implementation for persisting pulse checker state. | `dotnet add package Healthie.NET.CosmosDb` |
| **[Healthie.NET.Dashboard](https://www.nuget.org/packages/Healthie.NET.Dashboard)** | Blazor health monitoring dashboard (Razor Class Library, zero third-party dependencies). | `dotnet add package Healthie.NET.Dashboard` |
| **[Healthie.NET.Quartz](https://www.nuget.org/packages/Healthie.NET.Quartz)** | Quartz.NET `IPulseScheduler` implementation for CRON-based scheduling. | `dotnet add package Healthie.NET.Quartz` |
| **[Healthie.NET.Mcp](https://www.nuget.org/packages/Healthie.NET.Mcp)** | Model Context Protocol server, so an AI agent can read and act on service health. | `dotnet add package Healthie.NET.Mcp` |
| **[Healthie.NET.AI](https://www.nuget.org/packages/Healthie.NET.AI)** | Optional AI diagnostics that explain a checker's recent failures. Bring any `IChatClient`. | `dotnet add package Healthie.NET.AI` |

All packages target **.NET 8** and **.NET 10**.

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

// Scans the assembly for pulse checkers and registers the built-in PeriodicTimer
// scheduler and in-memory state provider.
builder.Services.AddHealthie(typeof(Program).Assembly);

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

The maximum number of history entries is configured globally via `HealthieOptions.MaxHistoryLength` (default: 10, range: 1-100). Values outside the range are clamped rather than rejected.

### Constructor Overloads

| Constructor | Description |
|---|---|
| `PulseChecker(IStateProvider)` | Default interval (`EveryMinute`), threshold `0`. |
| `PulseChecker(IStateProvider, PulseInterval)` | Custom interval, threshold `0`. |
| `PulseChecker(IStateProvider, PulseInterval, uint, ILogger?)` | Custom interval and threshold, with a logger for diagnostics. |
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

builder.Services.AddHealthie(typeof(Program).Assembly);
```

### With Quartz.NET Scheduling

For persistent, CRON-based scheduling with clustering support, use the Quartz provider:

```shell
dotnet add package Healthie.NET.Quartz
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

The `Healthie.NET.Dashboard` package is a pulse monitor for your services, shipped as a Razor Class Library. It is pure HTML and CSS with no third-party UI framework and no web fonts, so it renders the same on an air-gapped network as it does on your laptop.

**Key features:**

- **Live, event-driven** -- subscribes to `IPulseChecker.StateChanged` and updates one entry in place. No polling; a full state read happens only on first load.
- **Aggregate pulse trace** -- an EKG across the header, with the combined checks-per-minute of everything you monitor.
- **A row per checker** -- status light, name, checks per minute, and a pulse strip of the last N runs, one blip per run, coloured by the health it reported.
- **Detail panel** -- select a checker for its uptime, failure streak, last message, and its interval, failure threshold, group and tags, all editable in place.
- **Groups and tags** -- a checker sits in at most one group and carries any number of tags. Sections the list by group, filters it by tag. Both are declared in code and can be changed here; see [Groups and tags](#groups-and-tags).
- **Pin** -- keep the checkers worth watching at the top. A pin is shared, not personal.
- **Rows or cards** -- the same list laid out either way, flat or sectioned by group with per-group healthy/suspicious/failing tallies.
- **Event log** -- state transitions as they happen, so you can see what changed and when without reading logs. The expand icon opens it full size.
- **Legend and about** -- the `?` in the header explains every colour and control.
- **Per-checker and bulk control** -- run, pause, and reset one checker or all of them.
- **Search** -- filter the list by name as you type.
- **Dark by default** -- with a light theme a toggle away.
- **UTC throughout** -- the dashboard renders on the server, so a "local" time would be the server's, not yours. Every time it shows reads the same wherever you are.
- **Responsive** -- columns collapse and the panel stacks; usable down to 375px.

### Groups and tags

They look alike and answer different questions.

A checker belongs to **one group at most** -- that is where it lives. Groups are what the sectioned
view splits on, so every checker appears exactly once and a section's tallies add up.

A checker carries **any number of tags** -- those describe it and filter the list, and are free to
cut across groups: a `tier-1` tag can sit on checkers in three different groups.

Declare the defaults in code:

```csharp
public class RedisCachePulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override string DisplayName => "Redis Cache";

    public override string DefaultGroup => "Data Stores";

    public override IReadOnlyList<string> DefaultTags => ["tier-1", "cache"];

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => /* ... */;
}
```

Change them later from the dashboard, or in code through `SetGroupAsync` and `SetTagsAsync`. Either
way the change is stored through the `IStateProvider`, exactly as an interval or threshold change
is, so it outlives a restart. The defaults only seed a checker that has no stored state yet; they
never overwrite a change you made.

Tags are trimmed, de-duplicated case-insensitively, and ordered, so `" Tier-1 "` and `"tier-1"` are
one tag. A checker with no group gathers under `UNGROUPED`.

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

The dashboard subscribes to every checker's `StateChanged` event. A change updates that one entry and nothing else -- there is no polling and no periodic re-fetch, and the only full read of every checker's state happens on first load. A checker's state carries its own history, so the pulse strip costs no extra reads.

The interval and threshold you set from the panel are written through to the checker, so they outlive the page and take effect on the next run.

---

## Monitoring Existing Health Checks

If you already have health checks written for `Microsoft.Extensions.Diagnostics.HealthChecks` -- your own, or any of the community ones for SQL Server, Redis, RabbitMQ, Azure, AWS and so on -- Healthie.NET can monitor them as they are. One call adopts every health check you have registered:

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "orders-db")
    .AddRedis(redisConnectionString, name: "cache");

builder.Services.AddHealthie();

// Schedules every health check above, with a failure threshold, history, and the dashboard.
builder.Services.AddHealthieForHealthChecks(PulseInterval.Every30Seconds, unhealthyThreshold: 2);
```

Call it after the `AddHealthChecks()` calls that register them. To monitor one health check on its own terms, use `AddHealthieHealthCheck<TCheck>("name", ...)`.

A health check reports the state it is in right now; the threshold, the history, and the three-state model are what Healthie.NET adds on top.

> **On `Degraded`:** `HealthStatus.Degraded` maps to `Suspicious`, but the two do not mean quite the same thing. `Degraded` means impaired but working, whereas `Suspicious` means a failure that has not yet been confirmed by enough consecutive failures. With the default threshold of `0`, a degraded check is therefore reported as `Unhealthy` on its first failure. Give it a threshold of at least `1` to keep it reading as suspicious.

---

## Kubernetes Probes

```csharp
app.MapHealthieLiveness();   // GET /healthie/live  -- 200 while the process is up
app.MapHealthieReadiness();  // GET /healthie/ready -- 503 while any active checker is unhealthy
```

Liveness deliberately ignores checker state: it answers "is this process alive", not "is everything it monitors healthy". A liveness probe that fails because a database is down would have the orchestrator restart a process that is working correctly and correctly reporting a problem elsewhere. Readiness is the one that takes an instance out of rotation.

Pass `failOnSuspicious: true` to `MapHealthieReadiness()` to also refuse traffic while a checker is suspicious.

---

## AI Agents (MCP)

`Healthie.NET.Mcp` serves a [Model Context Protocol](https://modelcontextprotocol.io) endpoint, so an agent such as Claude or Copilot can read your service health and ask questions about it in plain language.

```shell
dotnet add package Healthie.NET.Mcp
```

```csharp
builder.Services.AddHealthieMcp();

var app = builder.Build();
app.MapHealthieMcp();   // /healthie/mcp
```

| Tool | Kind | Description |
|---|---|---|
| `get_health_states` | read | The current health of every monitored component. |
| `get_unhealthy_checkers` | read | Only the components that are unhealthy or suspicious. |
| `get_checker` | read | One component's health and configuration. |
| `get_check_history` | read | A component's recent run history, newest first, paged. |
| `run_check` | action | Runs a check now and returns the fresh result. |
| `reset_checker` | action | Clears a component's failure streak. |

The server is **read-only by default**. The two action tools appear only when you ask for them:

```csharp
builder.Services.AddHealthieMcp(options => options.AllowMutations = true);

app.MapHealthieMcp().RequireAuthorization();   // anything that reaches this can trigger checks
```

---

## AI Diagnostics

`Healthie.NET.AI` explains what a checker's recent history shows: whether it is failing consistently or intermittently, when it started, and what the errors point to. It is provider-agnostic -- it works against any `Microsoft.Extensions.AI` `IChatClient`, so the model is your choice.

```shell
dotnet add package Healthie.NET.AI
```

```csharp
// Any IChatClient: Anthropic, OpenAI, Azure OpenAI, or a local model via Ollama.
builder.Services.AddSingleton<IChatClient>(
    new AnthropicClient().AsIChatClient("claude-opus-4-8"));

builder.Services.AddHealthieAI();
```

```csharp
var diagnosis = await diagnostician.DiagnoseAsync("orders-db");

Console.WriteLine(diagnosis.Summary);
Console.WriteLine($"Failure rate rose: {diagnosis.Anomaly.IsAnomalous}");
```

The anomaly comparison alongside the summary is arithmetic, not a model call, so it is worth trusting on its own.

> **What leaves your process:** diagnosing a checker sends its name, health, and the messages its checks reported to whichever model you configured. Those messages are written by your own checkers, which is a good reason to keep credentials out of check results.

If your only consumer is an AI agent, you may not need this package at all: an agent talking to the MCP endpoint can read `get_check_history` and reason about it itself. This package is for the surfaces where there is no model in the loop.

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

The repository includes three sample applications demonstrating different usage patterns. All three run with **no external dependency**: without a CosmosDB connection string they use the in-memory state provider.

| Sample | Description | Path |
|---|---|---|
| **Console** | Minimal console application with interactive menu | [`samples/Healthie.Sample.Console`](https://github.com/ivanvyd/Healthie.NET/tree/main/samples/Healthie.Sample.Console) |
| **WebAPI** | REST endpoints, Swagger, Kubernetes probes, and the MCP endpoint | [`samples/Healthie.Sample.WebApi`](https://github.com/ivanvyd/Healthie.NET/tree/main/samples/Healthie.Sample.WebApi) |
| **BlazorUI** | Blazor Server app with the Healthie.NET UI dashboard | [`samples/Healthie.Sample.BlazorUI`](https://github.com/ivanvyd/Healthie.NET/tree/main/samples/Healthie.Sample.BlazorUI) |

```shell
dotnet run --project samples/Healthie.Sample.WebApi     # http://localhost:5199/healthie
```

Set `ConnectionStrings:CosmosDb` on the WebAPI or BlazorUI sample to exercise the durable path instead.

### Running everything together

To run the samples against a real CosmosDB, either use the [Aspire](https://aspire.dev) host, which starts the emulator and both samples and puts them on one dashboard:

```shell
aspire run --project samples/Healthie.AppHost
```

or Docker, which needs no .NET or Aspire tooling installed:

```shell
docker compose up --build     # API on :8080, dashboard on :8081
```

Both are development-time only. Nothing under `src/` depends on Aspire or Docker.

---

## Migration

Upgrading from v1.x? See the [v1 to v2 migration guide](https://github.com/ivanvyd/Healthie.NET/blob/main/docs/migration-v1-to-v2.md).

---

## Roadmap

Planned features for future releases:

- **Notifications on state transitions** -- webhooks in [CloudEvents](https://cloudevents.io) format, and recipes for Slack, Teams, and email. Firing on transitions rather than on every check is what the failure threshold makes possible.
- **OpenTelemetry** -- checker state, transition counts, and check duration as metrics and traces over OTLP, rather than an exporter per vendor.
- **Concurrency tokens on `IStateProvider`** -- state writes are currently last-write-wins, so a setting changed from the dashboard can be overwritten by a check that read the state first. Resolving it needs the interface to carry a version.
- **Arbitrary intervals** -- `PulseInterval` currently tops out at five minutes; a `TimeSpan` would remove the ceiling.
- **Checker grouping** -- organize pulse checkers into logical groups with group-level actions.
- **Additional state providers** -- SQL Server, PostgreSQL, Redis.
- **Additional scheduling providers** -- Temporal, for teams already running it and wanting durable, distributed scheduling.

---

## Contributing

Contributions are welcome. [CONTRIBUTING.md](https://github.com/ivanvyd/Healthie.NET/blob/main/CONTRIBUTING.md) covers building, testing, the coding standards, and how releases work; [CODE_OF_CONDUCT.md](https://github.com/ivanvyd/Healthie.NET/blob/main/CODE_OF_CONDUCT.md) applies to everyone taking part.

```shell
git clone https://github.com/ivanvyd/Healthie.NET.git
cd Healthie.NET
dotnet build Healthie.NET.sln
dotnet test Healthie.NET.sln
```

The samples need no external infrastructure, so `dotnet run --project samples/Healthie.Sample.Console` is enough to see a change work end to end.

Please open an issue before starting anything large, so the design can be agreed before you spend time on it.

## Support

| | |
|---|---|
| **Bug or feature request** | [Open an issue](https://github.com/ivanvyd/Healthie.NET/issues/new/choose) |
| **Question or idea** | [Start a discussion](https://github.com/ivanvyd/Healthie.NET/discussions) |
| **Security vulnerability** | Report it privately -- see [SECURITY.md](https://github.com/ivanvyd/Healthie.NET/blob/main/SECURITY.md). Please don't open a public issue. |
| **What changed** | [CHANGELOG.md](https://github.com/ivanvyd/Healthie.NET/blob/main/CHANGELOG.md) |

## License

[MIT](https://github.com/ivanvyd/Healthie.NET/blob/main/LICENSE) -- do what you like with it, no warranty.
