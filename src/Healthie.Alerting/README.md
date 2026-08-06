![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Alerting

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Alerting.svg)](https://www.nuget.org/packages/Healthie.NET.Alerting)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

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
    .AddHealthieSlackAlerts(new Uri("https://hooks.slack.com/services/..."))
    .AddHealthiePagerDutyAlerts("your-integration-key");
```

Add as many sinks as you like; each gets every alert. For anywhere none of them reaches, implement `IAlertSink` and register it — the dispatcher finds every one that is registered.

## Where alerts go

| Sink | Registered with | Sends |
|---|---|---|
| Slack | `AddHealthieSlackAlerts(url)` | A message with a colour-coded attachment, to an incoming webhook |
| Microsoft Teams | `AddHealthieMicrosoftTeamsAlerts(url)` | An Adaptive Card, to a **Workflows** URL |
| PagerDuty | `AddHealthiePagerDutyAlerts(key)` | An Events API v2 `trigger`, resolved when the checker recovers |
| Anything else | `AddHealthieWebhookAlerts(url)` | The generic JSON payload below |

Three of these are not the webhook wearing a hat. Slack, Teams and PagerDuty each reject arbitrary JSON, so posting the generic payload at them fails — which is why reaching them used to need something in between to reshape it. None needs a dependency this package did not already have, so they are here rather than in a package each.

**Teams takes the Workflows URL, not an Office 365 connector.** Microsoft has retired those connectors along with the `MessageCard` payload they accepted; in Teams, add a *Workflow* to the channel and use the URL it gives you.

**PagerDuty closes what it opens.** A failure raises an incident keyed on the checker, and the recovery resolves that same incident rather than posting a second message saying everything is fine. A checker flapping between suspicious and unhealthy stays one incident, because the deduplication key deliberately leaves out the health and the time. `Suspicious` maps to `warning` rather than `critical` — it is the state that means "failing, but not past the threshold yet", and paging on it would defeat having a threshold.

Configure any sink's `HttpClient` — its timeout, its handler, an auth header — by naming its `HttpClientName` constant in your own `AddHttpClient` call.

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
