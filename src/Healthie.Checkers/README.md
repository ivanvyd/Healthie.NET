![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Checkers

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Checkers.svg)](https://www.nuget.org/packages/Healthie.NET.Checkers)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

Ready-made pulse checkers for the things nearly every application ends up watching, so there is no checker code to write.

## Installation

```shell
dotnet add package Healthie.NET.Checkers
```

## Usage

```csharp
using Healthie.Abstractions.Scheduling;
using Healthie.Checkers;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieHttpChecker("payments-api", new Uri("https://payments.internal/health"),
        PulseSchedule.Every(TimeSpan.FromSeconds(30)))
    .AddHealthieTcpChecker("postgres", "db.internal", 5432,
        PulseSchedule.Every(TimeSpan.FromMinutes(1)))
    .AddHealthieCertificateChecker("public-tls", "example.com",
        PulseSchedule.Cron("0 3 * * *"), warnWithin: TimeSpan.FromDays(30))
    .AddHealthieDnsChecker("apex-dns", "example.com",
        PulseSchedule.Every(TimeSpan.FromMinutes(5)))
    .AddHealthieDiskSpaceChecker("data-volume", "/data",
        PulseSchedule.Cron("*/15 * * * *"));
```

Each call takes a **name**. The same type is usually registered several times — three endpoints, two drives — and the name is what separates them in storage, in the REST API and on the dashboard.

Unlike checkers you write yourself, these are not found by assembly scanning: each has to be told what to watch.

## The checkers

| Call | Watches | Healthy when |
|---|---|---|
| `AddHealthieHttpChecker` | An HTTP endpoint | The status passes your predicate (any 2xx by default) |
| `AddHealthieTcpChecker` | A TCP port | The port accepts a connection within the timeout |
| `AddHealthieCertificateChecker` | TLS certificate expiry | More than `warnWithin` left |
| `AddHealthieDnsChecker` | Name resolution | The name resolves to at least one address |
| `AddHealthieDiskSpaceChecker` | Free disk space | Above the warning threshold |

## Two of them use all three states

Certificate expiry and disk space are gradual and knowable in advance, so both report **suspicious** before they report unhealthy — a certificate inside its warning window, a drive below the warning threshold but above the critical one. That warning is the useful signal; by the time either is unhealthy it is already an outage.

The others are binary and only ever report healthy or unhealthy. Give them an `unhealthyThreshold` if you want a blip to read as suspicious rather than an outage.

## HTTP client configuration

The HTTP checker resolves its client from `IHttpClientFactory` under the name `HttpPulseChecker.HttpClientName`. Configure its timeout, handler or retry policy by naming it in your own registration:

```csharp
builder.Services.AddHttpClient(HttpPulseChecker.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

The request reads headers only — a health endpoint that returns a body is common, and downloading it to discard it is work this check does not need to do.
