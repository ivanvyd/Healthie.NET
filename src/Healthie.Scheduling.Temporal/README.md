![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Temporal

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Temporal.svg)](https://www.nuget.org/packages/Healthie.NET.Temporal)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Schedules pulse checks with Temporal, so the schedule lives in the cluster rather than in the process.

## Installation

```shell
dotnet add package Healthie.NET.Temporal
```

## Usage

```csharp
using Healthie.Scheduling.Temporal;
using Temporalio.Extensions.Hosting;

builder.Services.AddSingleton<ITemporalClient>(await TemporalClient.ConnectAsync(new("localhost:7233")));

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieTemporal(options => options.TaskQueue = "healthie");

// A worker must be listening on the same queue, or the schedules fire and nothing runs.
builder.Services
    .AddHostedTemporalWorker("healthie")
    .AddScopedActivities<PulseCheckerActivities>()
    .AddWorkflow<PulseCheckerWorkflow>();
```

The Temporal client is yours to create: its address, namespace, TLS and API key are your decisions, and a library that picked them would be picking which cluster your workflows run on.

## What it buys, and what it costs

**Buys:** the schedule survives a restart and a redeploy, each occurrence is handed to exactly one worker however many replicas are running, and every run has a history somebody can look at.

**Costs:** a Temporal cluster. If you do not already run one, this is the wrong trade — [Healthie.NET.Hangfire](https://www.nuget.org/packages/Healthie.NET.Hangfire) gives you the same survives-a-restart, runs-once-across-replicas properties against a database you probably already have, and the built-in timer needs nothing at all.

The SDK also carries a native Rust core, so it is a large dependency: it adds a few hundred megabytes to a build output. Worth knowing before adding it to something small.

## Details

Schedules are named `healthie-<checker name>`, so they are recognisable in the Temporal UI and cannot collide with your own.

Overlap policy is `Skip`. A checker already refuses to run on top of itself, so buffering would queue occurrences that each return immediately — and a check that is late is worth less than the next one, which is current.

Cron expressions pass through untranslated: Temporal parses standard Unix cron, the same syntax Healthie.NET uses. Fixed periods become interval specs, which Temporal counts from an epoch rather than from creation time, so replicas agree on when a schedule fires.

The workflow does nothing but call an activity — workflow code must be deterministic, and a pulse check is not — and the activity is given the checker's *name*, because a schedule outlives the process that created it and a checker does not survive being serialized.

## Testing

The mapping from a schedule to a Temporal specification is unit-tested. Everything else needs a running cluster, so it is not exercised in the default test suite — that suite deliberately requires no infrastructure. Run it against a dev server (`temporal server start-dev`) when changing this package.
