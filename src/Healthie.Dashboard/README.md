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
- Summary stat cards with color-coded health counts
- Dark/light theme toggle
- Search and filter by name
- Mobile responsive (375px+)
- CSS-only animations

## Configuration Options

| Option | Type | Default | Description |
|---|---|---|---|
| `DashboardTitle` | `string` | `"System Health"` | Title displayed at the top of the dashboard. |
| `EnableDarkModeToggle` | `bool` | `true` | Whether the dark/light mode toggle is visible. |

## See Also

[Back to main README](../../README.md)
