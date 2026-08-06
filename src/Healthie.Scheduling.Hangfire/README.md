![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Hangfire

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Hangfire.svg)](https://www.nuget.org/packages/Healthie.NET.Hangfire)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

Hangfire `IPulseScheduler` implementation for Healthie.NET. Schedules each pulse checker as a Hangfire recurring job.

## Installation

```shell
dotnet add package Healthie.NET.Hangfire
```

## Usage

Configure Hangfire as you normally would, then add one line:

```csharp
using Healthie.Scheduling.Hangfire;

builder.Services.AddHangfire(c => c.UseSqlServerStorage(connectionString));
builder.Services.AddHangfireServer();

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieHangfire();
```

Hangfire's storage, retry policy and dashboard stay yours to configure — a library that picked them would be picking where your job data lives.

## What it buys you

The schedule lives in storage rather than in the process, so it survives a restart. And across several replicas each occurrence is handed to exactly one server, so a scaled-out deployment runs each check once rather than once per replica — which the built-in timer does not do.

Recurring jobs appear in the Hangfire dashboard under the `healthie:` prefix, alongside their run history.

## Granularity

Hangfire notices due work by polling, every 15 seconds by default, so a check asking to run more often than that will not. Lower `BackgroundJobServerOptions.SchedulePollingInterval`, or keep the fast checks on the built-in timer.

Hangfire schedules only by cron, so a fixed period has to become a cron expression. Periods that divide evenly into a minute, an hour or a day convert; seven seconds does not, and is refused rather than approximated — a cron expression firing at :00, :07 … :56 would wait four seconds before starting the next minute. Give such a checker a cron expression of its own instead.

Cron expressions pass through untouched: Hangfire parses cron with Cronos, the same standard Unix syntax Healthie.NET uses.
