![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Uptime

> ### Deprecated as of 4.1.0
>
> Uptime reporting now ships in **[Healthie.NET](https://www.nuget.org/packages/Healthie.NET)**, the
> core package. Call `AddHealthieUptime()` there and drop this reference — uptime over any window
> works exactly as it does here.
>
> **Nothing breaks if you keep it.** This package is still published, as an assembly of type
> forwards, so an application referencing it keeps compiling and running untouched. It carried no
> third-party dependency, so keeping it separate cost you an install and saved you nothing. It will
> not gain features.

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Uptime.svg)](https://www.nuget.org/packages/Healthie.NET.Uptime)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Answers "what was our uptime last quarter" — which the rolling history cannot, because it holds the last hundred results and for a one-second checker that is the last hundred seconds.

## Installation

```shell
dotnet add package Healthie.NET.Uptime
```

## Usage

```csharp
using Healthie.Uptime;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieUptime();
```

Then ask:

```csharp
public sealed class SlaReport(IUptimeStore store)
{
    public async Task<UptimeReport> LastQuarterAsync(string checkerName)
    {
        var to = DateTime.UtcNow;
        var from = to.AddDays(-90);

        var segments = await store.GetSegmentsAsync(checkerName, from, to);

        return UptimeCalculator.Calculate(checkerName, segments, from, to);
    }
}
```

```csharp
report.UptimePercentage   // 99.94
report.Met(99.9)          // true
report.Unhealthy          // 00:47:31
report.Unknown            // 00:12:05  -- see below
```

## It stores transitions, not checks

A checker running every second produces 86,400 results a day and perhaps four transitions. Both can answer "how long was it down", but only one is exact and only one is small enough to keep for a year.

That is also why this is a separate store from `IStateProvider`: state is one small document per checker, overwritten on every check; this is an append-only series read occasionally and kept for months. Putting them together would mean every check rewriting a document that grows for ever.

## Unknown time is neither up nor down

Time when the application was not running is time nothing was watching.

Counting it as downtime would report an outage for every deployment. Counting it as uptime would claim a component was fine over a period nobody looked at it. So it is neither: it is reported separately as `Unknown`, and `UptimePercentage` is measured against **observed** time.

A checker that never ran has `UptimePercentage == null` rather than 0 or 100 — either number would be a claim about a period with no evidence.

## Storage

`InMemoryUptimeStore` is the default, so uptime works out of the box. It does not survive a restart. Implement `IUptimeStore` and register it before or after `AddHealthieUptime()` — registration is order-independent, as with the state providers.

Recording cannot hurt a check. A store that is slow, remote or briefly unavailable is not the component being monitored, so transitions go through a bounded queue and are written on the recorder's own loop; a store that throws loses one transition rather than ending recording.
