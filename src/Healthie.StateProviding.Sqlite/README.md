![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Sqlite

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Sqlite.svg)](https://www.nuget.org/packages/Healthie.NET.Sqlite)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

SQLite `IStateProvider` implementation for Healthie.NET. Durable pulse checker state that survives a restart, with no server to stand up.

## Installation

```shell
dotnet add package Healthie.NET.Sqlite
```

## Usage

```csharp
using Healthie.StateProviding.Sqlite;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieSqlite("Data Source=healthie.db");
```

The table is created on startup if it does not exist, and so is the database file.

## When not to use it

SQLite serialises writers. A deployment running several replicas against one file will contend on every check, so reach for [`Healthie.NET.Postgres`](https://www.nuget.org/packages/Healthie.NET.Postgres) or [`Healthie.NET.SqlServer`](https://www.nuget.org/packages/Healthie.NET.SqlServer) there. On a single node, or in a sample, it is the least infrastructure that still outlives a restart.

## Other engines

This is a thin wrapper over [`Healthie.NET.Relational`](https://www.nuget.org/packages/Healthie.NET.Relational), which works against any database with an ADO.NET driver.
