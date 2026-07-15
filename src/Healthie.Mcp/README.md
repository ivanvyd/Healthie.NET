![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Mcp

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Mcp.svg)](https://www.nuget.org/packages/Healthie.NET.Mcp)

A [Model Context Protocol](https://modelcontextprotocol.io) server for Healthie.NET, so an AI agent such as Claude, Copilot, or Cursor can read your service health and act on it in plain language. Read-only until you say otherwise.

## Installation

```shell
dotnet add package Healthie.NET.Mcp
```

## Usage

```csharp
using Healthie.Mcp;

builder.Services.AddHealthie(typeof(Program).Assembly);
builder.Services.AddHealthieMcp();

var app = builder.Build();
app.MapHealthieMcp();   // /healthie/mcp
app.Run();
```

Point an MCP client at `http://localhost:5000/healthie/mcp` and ask it what is unhealthy.

## Tools

| Tool | Kind | Description |
|---|---|---|
| `get_health_states` | read | The current health of every monitored component. |
| `get_unhealthy_checkers` | read | Only the components that are unhealthy or suspicious. |
| `get_checker` | read | One component's health and configuration. |
| `get_check_history` | read | A component's recent run history, newest first, paged. |
| `run_check` | action | Runs a check now and returns the fresh result. |
| `reset_checker` | action | Clears a component's failure streak. |

## Mutations Are Opt-In

The server exposes only the read tools by default. The two action tools appear when you ask for them, and anything that can reach the endpoint can then trigger checks -- so require authorization when you do:

```csharp
builder.Services.AddHealthieMcp(options => options.AllowMutations = true);

app.MapHealthieMcp().RequireAuthorization();
```

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `AllowMutations` | `bool` | `false` | Whether `run_check` and `reset_checker` are exposed. |
| `MaxHistoryPageSize` | `int` | `50` | Caps how many history entries one `get_check_history` call returns. |

## Transport

Served over Streamable HTTP, statelessly, so it scales out like any other endpoint in your app. The MCP C# SDK also supports a stdio host if you want an agent to run the server locally.

## See Also

[Back to main README](https://github.com/ivanvyd/Healthie.NET#ai-agents-mcp)
