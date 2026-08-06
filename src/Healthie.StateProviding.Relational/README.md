![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Relational

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Relational.svg)](https://www.nuget.org/packages/Healthie.NET.Relational)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

Relational `IStateProvider` implementation for Healthie.NET. Persists pulse checker state to any database with an ADO.NET driver — bring your own, or install one of the ready-made wrappers.

## Installation

```shell
dotnet add package Healthie.NET.Relational
```

Most people want one of these instead, which supply the driver and the dialect for you:

| Package | Engine |
|---|---|
| [Healthie.NET.Postgres](https://www.nuget.org/packages/Healthie.NET.Postgres) | PostgreSQL, including Databricks Lakebase |
| [Healthie.NET.SqlServer](https://www.nuget.org/packages/Healthie.NET.SqlServer) | SQL Server, Azure SQL |
| [Healthie.NET.Sqlite](https://www.nuget.org/packages/Healthie.NET.Sqlite) | SQLite |

## Usage

Reach for this package directly when your database is not one of the three above. Supply a connection factory and a dialect:

```csharp
using Healthie.StateProviding.Relational;
using MySqlConnector;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieRelational(
        () => new MySqlConnection(connectionString),
        new RelationalDialect(
            "MySQL",
            "CREATE TABLE IF NOT EXISTS {0} (" +
                "name VARCHAR(191) NOT NULL PRIMARY KEY, state_type TEXT NULL, value LONGTEXT NOT NULL)",
            "INSERT INTO {0} (name, state_type, value) VALUES (@name, @state_type, @value) " +
                "ON DUPLICATE KEY UPDATE state_type = VALUES(state_type), value = VALUES(value)"));
```

A dialect needs two statements: one that creates the table only if it is missing, because it runs on every start; and one that inserts or replaces a single row. Reading is identical on every engine, so it is not part of the dialect.

State is stored as JSON in one table keyed by checker name, so the schema does not change when the state model does.

## Table name

Defaults to `healthie_pulse_state`, and may be schema-qualified. It is validated as a plain identifier before it reaches the SQL, because no database allows an identifier to be parameterised — anything else is refused rather than interpolated.
