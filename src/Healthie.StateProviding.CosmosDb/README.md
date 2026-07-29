![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.CosmosDb

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.CosmosDb.svg)](https://www.nuget.org/packages/Healthie.NET.CosmosDb)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Azure CosmosDB `IStateProvider` implementation for Healthie.NET. Persists pulse checker state to CosmosDB for durable storage across application restarts and distributed environments.

## Installation

```shell
dotnet add package Healthie.NET.CosmosDb
```

## Usage

```csharp
using Healthie.StateProviding.CosmosDb;
using Microsoft.Azure.Cosmos;

var cosmosClient = new CosmosClient("your-connection-string");

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieCosmosDb(cosmosClient, "your-database", "healthie-state");
```

The container is created on startup if it does not exist. The database is not, so create it yourself or point at an existing one.

If you would rather build the `Container` yourself, the original overload still works:

```csharp
var container = cosmosClient.GetContainer("your-database", "healthie-state");

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieCosmosDb(container);
```

> **Important:** The CosmosDB container must use `/id` as the partition key path. Startup fails with a clear error if an existing container is partitioned differently.

## Key Types

| Type | Description |
|---|---|
| `StartupExtensions.AddHealthieCosmosDb()` | Registers `CosmosDbStateProvider` as the singleton `IStateProvider` and `CosmosDbStateProviderInitializer` as an `IStateProviderInitializer`. |
| `CosmosDbStateProvider` | Implements `IStateProvider` using CosmosDB `ReadItemAsync` / `UpsertItemAsync`. |
| `CosmosDbStateProviderInitializer` | Creates the container on startup if it is missing and validates its partition key path. |

## How It Works

- Each pulse checker's state is stored as a document with the checker's fully-qualified name as both the `id` and partition key.
- `GetStateAsync` reads the document by id; returns `default` on `404 NotFound`.
- Each document records the assembly-qualified type of the state it holds, and reading it as a different type throws rather than returning a mismatched state. Documents written before the type was recorded carry no type and are read as-is.
- `SetStateAsync` upserts the document, creating or replacing it atomically.

## Concurrency

Writes can be made conditional. `GetStateEntryAsync` returns the state together with the document's ETag, and `TrySetStateAsync` sends it as `If-Match`, so a write made from a state that has since changed is refused (CosmosDB answers `412 PreconditionFailed`) rather than silently overwriting whoever changed it. When nothing is stored yet there is no ETag to match, so the write becomes a create, which CosmosDB refuses a second time with `409 Conflict` — the same guarantee at the one moment there is nothing to compare.

A refused write is reported as `false`, not thrown. Under contention losing is the expected outcome and the answer is always the same: read again, reapply, write again. `StateProviderExtensions.UpdateStateAsync` is that loop. Both writers go through it — the setting change *and* the check storing its result — which is what makes the guarantee hold in both directions: a check that read the state before a setting changed no longer writes the old setting back over it.

`SetStateAsync` still upserts unconditionally. It replaces a whole document with one the caller supplies, so there is no version to carry and nothing to compare — it is the escape hatch, not the path the library takes.

## See Also

[Back to main README](../../README.md)
