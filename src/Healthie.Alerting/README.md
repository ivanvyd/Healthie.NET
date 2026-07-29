![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Alerting

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Alerting.svg)](https://www.nuget.org/packages/Healthie.NET.Alerting)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Turns health changes into alerts and delivers them, without letting a delivery problem become a monitoring problem.

## Installation

```shell
dotnet add package Healthie.NET.Alerting
```

## Usage

```csharp
using Healthie.Alerting;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieAlerts()
    .AddHealthieWebhookAlerts(new Uri("https://hooks.example.com/healthie"));
```

Add as many webhooks as you like; each gets every alert. For anywhere a webhook cannot reach, implement `IAlertSink` and register it — the dispatcher finds every one that is registered.

## What alerts

By default, a checker becoming **unhealthy**, and a checker **recovering**. Not every check — only a change of health.

```csharp
.AddHealthieAlerts(options =>
{
    options.MinimumSeverity = PulseCheckerHealth.Suspicious;  // include early warnings
    options.SendRecoveries = false;                            // opens only, no all-clears
    options.DeduplicationWindow = TimeSpan.FromMinutes(15);
})
```

`MinimumSeverity` defaults to `Unhealthy`. Suspicious is the state a checker passes through on its way there, so alerting on it by default would page somebody for every blip the failure threshold exists to absorb. Lower it when the early warning is the point — a certificate inside its expiry window is suspicious for weeks and never becomes unhealthy until it is too late to act.

`DeduplicationWindow` defaults to five minutes. A component on the edge of working flaps, and every flip is a genuine health change; without a window a checker running every second could send a hundred alerts about one incident.

## Delivery cannot hurt the checks

This is the part worth knowing. Alerting subscribes to state changes rather than sitting inside the check, so:

- **A sink that throws** is logged and skipped. The other sinks still get the alert, and the next alert still goes out.
- **A sink that hangs** is abandoned at `DeliveryTimeout` (ten seconds by default).
- **A slow sink never delays a check.** Raising the event only queues; every delivery happens on the dispatcher's own loop.
- **A backed-up queue drops rather than grows.** `QueueCapacity` bounds it at 1024, and drops are counted and logged. An unbounded queue would trade a delivery problem for a memory leak inside the process being monitored.

None of it can mark a component unhealthy. A checker reports on the thing it watches, and a broken webhook is not that thing.

## Webhook payload

```json
{
  "checker": "Contoso.Api.DatabasePulseChecker",
  "displayName": "Primary database",
  "group": "data",
  "tags": ["cloud", "primary"],
  "status": "Unhealthy",
  "previousStatus": "Healthy",
  "isRecovery": false,
  "message": "Connection timed out after 5s.",
  "occurredAt": "2026-07-29T11:04:22.1234567Z",
  "deduplicationKey": "healthie:Contoso.Api.DatabasePulseChecker"
}
```

`deduplicationKey` identifies the ongoing incident, not the occurrence — it deliberately excludes the health and the time, so an incident tracker keyed on it closes the same incident it opened rather than accumulating one per transition.

Configure the client — timeout, handler, an auth header — by naming `WebhookAlertSink.HttpClientName` in your own `AddHttpClient` call.
