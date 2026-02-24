<p align="center">
  <img src="../../healthie.net.banner.png" alt="Healthie.NET - Trust your uptime" />
</p>

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
var container = cosmosClient.GetContainer("your-database", "healthie-state");

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieDefaultScheduler()
    .AddHealthieCosmosDb(container);
```

> **Important:** The CosmosDB container must use `/id` as the partition key path.

## Key Types

| Type | Description |
|---|---|
| `StartupExtensions.AddHealthieCosmosDb()` | Registers `CosmosDbStateProvider` as the singleton `IStateProvider`. |
| `CosmosDbStateProvider` | Implements `IStateProvider` using CosmosDB `ReadItemAsync` / `UpsertItemAsync`. |

## How It Works

- Each pulse checker's state is stored as a document with the checker's fully-qualified name as both the `id` and partition key.
- `GetStateAsync` reads the document by id; returns `default` on `404 NotFound`.
- `SetStateAsync` upserts the document, creating or replacing it atomically.

## See Also

[Back to main README](../../README.md)
