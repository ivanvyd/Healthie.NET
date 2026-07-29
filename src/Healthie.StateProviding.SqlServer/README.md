![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.SqlServer

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.SqlServer.svg)](https://www.nuget.org/packages/Healthie.NET.SqlServer)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

SQL Server `IStateProvider` implementation for Healthie.NET. Persists pulse checker state to SQL Server or Azure SQL for durable storage across application restarts and across the replicas of a scaled-out deployment.

## Installation

```shell
dotnet add package Healthie.NET.SqlServer
```

## Usage

```csharp
using Healthie.StateProviding.SqlServer;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieSqlServer(builder.Configuration.GetConnectionString("Healthie")!);
```

The table is created on startup if it does not exist. The database is not, so create it yourself or point at an existing one. Pass a second argument to store state somewhere other than `healthie_pulse_state`:

```csharp
.AddHealthieSqlServer(connectionString, "monitoring.pulse_state");
```

Writes update first and insert only if nothing was updated, under `UPDLOCK, SERIALIZABLE`. Those hints are what make concurrent writers safe rather than occasionally colliding on the primary key.

The checker name is the primary key, so it is capped at 450 characters — the longest a SQL Server key column may be. A checker name defaults to its type's full name and does not come close.

## Other engines

This is a thin wrapper over [`Healthie.NET.Relational`](https://www.nuget.org/packages/Healthie.NET.Relational), which works against any database with an ADO.NET driver.
