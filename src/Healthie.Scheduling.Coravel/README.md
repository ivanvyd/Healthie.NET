![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Coravel

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Coravel.svg)](https://www.nuget.org/packages/Healthie.NET.Coravel)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Runs pulse checks on Coravel's scheduler, so an application already using Coravel keeps one scheduler rather than two.

## Installation

```shell
dotnet add package Healthie.NET.Coravel
```

## Usage

```csharp
using Healthie.Scheduling.Coravel;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieCoravel();

var app = builder.Build();

app.Services.UseHealthiePulseScheduler();
```

## What this actually does, and when not to use it

Read this before choosing it.

**Coravel has no API for removing a scheduled job.** Its `IScheduler` exposes only `Schedule` methods — verified against Coravel 6.0.2. `IPulseScheduler` requires the opposite: checkers are scheduled, rescheduled and unscheduled while the application runs, from the dashboard and the REST API.

So this package does **not** register a Coravel job per checker. It registers **one** job that runs every second and asks Healthie which checkers are due; the due times live in Healthie, not in Coravel. Coravel supplies the tick and its host lifetime, and Healthie decides what runs.

That is a real integration and it works — schedules, reschedules and unschedules all behave — but it is worth being clear that Coravel is doing less here than it does for your own jobs.

**If you are not already using Coravel, use the built-in timer scheduler instead.** It does the same thing with no dependency. This package earns its place only when Coravel is already in the application and you would rather have one scheduler than two.

## Granularity

The tick runs every second, so a checker cannot run more often than that. Anything from one second upward works, including cron expressions — standard Unix syntax, five fields or six with a leading seconds field.

`PreventOverlapping` stops a slow tick being started again on top of itself. Checkers run one after another within a tick rather than in parallel, so a slow check cannot delay the next tick for everything else; each checker already refuses to overlap itself.
