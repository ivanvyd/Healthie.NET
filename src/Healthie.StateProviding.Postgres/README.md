![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Postgres

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Postgres.svg)](https://www.nuget.org/packages/Healthie.NET.Postgres)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

PostgreSQL `IStateProvider` implementation for Healthie.NET. Persists pulse checker state to PostgreSQL for durable storage across application restarts and across the replicas of a scaled-out deployment.

## Installation

```shell
dotnet add package Healthie.NET.Postgres
```

## Usage

```csharp
using Healthie.StateProviding.Postgres;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthiePostgres(builder.Configuration.GetConnectionString("Healthie")!);
```

The table is created on startup if it does not exist. The database is not, so create it yourself or point at an existing one. Pass a second argument to store state somewhere other than `healthie_pulse_state`:

```csharp
.AddHealthiePostgres(connectionString, "monitoring.pulse_state");
```

## Databricks Lakebase

Lakebase is managed PostgreSQL, so this package connects to it with no Databricks-specific code — use the Lakebase connection string and nothing else changes.

## Other engines

This is a thin wrapper over [`Healthie.NET.Relational`](https://www.nuget.org/packages/Healthie.NET.Relational), which works against any database with an ADO.NET driver. Use it directly for MySQL, Oracle, or anything else.
