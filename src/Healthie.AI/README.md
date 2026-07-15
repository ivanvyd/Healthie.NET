![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.AI

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.AI.svg)](https://www.nuget.org/packages/Healthie.NET.AI)

Explains what a pulse checker's recent history shows: whether it is failing consistently or intermittently, when it started, and what the errors point to. Provider-agnostic -- it depends on `Microsoft.Extensions.AI.Abstractions` and nothing else, so the model is your choice.

## Installation

```shell
dotnet add package Healthie.NET.AI
```

## Usage

Register any `IChatClient`, then add the diagnostician:

```csharp
using Healthie.AI;

// Anthropic, OpenAI, Azure OpenAI, or a local model via Ollama -- this package depends on none of them.
builder.Services.AddSingleton<IChatClient>(
    new AnthropicClient().AsIChatClient("claude-opus-4-8"));

builder.Services.AddHealthieAI();
```

Then ask it about a checker:

```csharp
var diagnosis = await diagnostician.DiagnoseAsync("MyApp.Pulses.DatabasePulseChecker");

Console.WriteLine(diagnosis.Summary);
Console.WriteLine($"Failure rate rose: {diagnosis.Anomaly.IsAnomalous}");
```

## Key Types

| Type | Description |
|---|---|
| `IPulseDiagnostician` | `DiagnoseAsync(name)` returns a `PulseDiagnosis` for one checker. |
| `PulseDiagnosis` | The checker's `Name`, a plain-English `Summary`, and an `Anomaly` report. |
| `AnomalyReport` | `IsAnomalous`, `RecentFailureRate`, `EarlierFailureRate`. |
| `StartupExtensions.AddHealthieAI()` | Registers `IPulseDiagnostician`. Requires an `IChatClient` in DI. |

## The Anomaly Report Is Not a Model Call

`Anomaly` compares the recent failure rate against the earlier one arithmetically, from the checker's own history. It is deterministic and testable, and it is worth trusting on its own -- including when no model is configured to write the summary.

## What Leaves Your Process

Diagnosing a checker sends its name, its health, and the messages its checks reported to whichever model you configured. Those messages are written by your own checkers, which is a good reason to keep credentials out of check results. This package never mutates state.

## You May Not Need This

If your only consumer is an AI agent, use [Healthie.NET.Mcp](https://www.nuget.org/packages/Healthie.NET.Mcp) instead: an agent talking to the MCP endpoint can read `get_check_history` and reason about it itself. This package is for the surfaces where there is no model in the loop -- a REST response, a dashboard, an alert.

## See Also

[Back to main README](https://github.com/ivanvyd/Healthie.NET#ai-diagnostics)
