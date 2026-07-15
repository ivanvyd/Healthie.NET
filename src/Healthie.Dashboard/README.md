![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Dashboard

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Dashboard.svg)](https://www.nuget.org/packages/Healthie.NET.Dashboard)

Zero-dependency Blazor Server dashboard for monitoring Healthie.NET pulse checkers in real time. Delivered as a Razor Class Library with pure HTML/CSS -- no third-party UI frameworks required.

## Installation

```shell
dotnet add package Healthie.NET.Dashboard
```

## Setup

**1. Register services in `Program.cs`:**

```csharp
using Healthie.Dashboard;

builder.Services.AddHealthieUI(options =>
{
    options.DashboardTitle = "System Health";
    options.EnableDarkModeToggle = true;
});
```

**2. Add static file middleware** (if not already present):

```csharp
app.UseStaticFiles();
```

**3a. Standalone endpoint** (non-Blazor apps) -- map the dashboard route:

```csharp
app.MapHealthieUI();                                   // Serves at /healthie/dashboard
app.MapHealthieUI().RequireAuthorization("AdminPolicy"); // With auth
```

**3b. Embedded component** (Blazor apps) -- render directly in a Razor page:

```razor
@page "/healthie/dashboard"
@using Healthie.Dashboard.Components

<HealthieDashboard />
```

> For interactive components, the host `Routes` must have `@rendermode="InteractiveServer"` in `App.razor`.

## Features

- Event-driven real-time updates via `IPulseChecker.StateChanged` (no polling)
- Per-checker management: start, stop, trigger, reset, change interval, change threshold
- Bulk actions: Start All, Stop All, Trigger All
- Groups and tags, both editable here and seeded from code — see below
- Pin a checker to the top of the list
- Rows or cards, flat or sectioned by group with per-group tallies
- Live event log, with a full-size view behind the expand icon
- Legend and about behind the `?` in the header
- Dark/light theme toggle
- Search and filter by name
- Mobile responsive (375px+)
- CSS-only animations, and no JavaScript of its own
- Every time shown is UTC

## Groups and tags

The two look similar and answer different questions.

A checker belongs to **one group at most**. Groups are what the sectioned view splits on, so
every checker appears exactly once and a section's tallies add up to what is under it.

A checker carries **any number of tags**. Tags describe it and filter the list, and are free to
cut across groups — a `tier-1` tag can sit on checkers in three different groups.

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

Both can then be changed from the dashboard's side panel, and the change is stored through the
`IStateProvider` like an interval or a threshold — so it outlives a restart. The defaults only
seed a checker with no stored state; they never overwrite a change made here.

Tags are trimmed, de-duplicated case-insensitively, and ordered, so `" Tier-1 "` and `"tier-1"`
are one tag. A blank group means no group, and those checkers gather under `UNGROUPED`.

## Configuration Options

| Option | Type | Default | Description |
|---|---|---|---|
| `DashboardTitle` | `string` | `"System Health"` | Title displayed at the top of the dashboard. |
| `EnableDarkModeToggle` | `bool` | `true` | Whether the dark/light mode toggle is visible. |

## See Also

[Back to main README](../../README.md)
