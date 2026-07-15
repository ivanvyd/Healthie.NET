![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.CosmosDb

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.CosmosDb.svg)](https://www.nuget.org/packages/Healthie.NET.CosmosDb)

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

Writes are last-write-wins. When two writers read the same state and write it back concurrently — a scheduled check and a dashboard-initiated setting change, say — whichever writes last is kept, and the other's change is lost.

For check results that is the wanted behavior: the most recent result is the interesting one. For setting changes it is a real limitation. Resolving it needs a concurrency token on `IStateProvider` itself, which is planned for the next major version; guarding the write with an ETag underneath the current interface can only turn a lost update into a failed write, and a failed write is recorded as a failed health check.

## See Also

[Back to main README](../../README.md)
