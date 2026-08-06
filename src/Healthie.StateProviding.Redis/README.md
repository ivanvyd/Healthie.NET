![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.Redis

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.Redis.svg)](https://www.nuget.org/packages/Healthie.NET.Redis)

**▶ [Live demo — board.healthie-dotnet.dev](https://board.healthie-dotnet.dev)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages. Full documentation at **[healthie-dotnet.dev](https://healthie-dotnet.dev)**.

Redis state provider for [Healthie.NET](https://github.com/ivanvyd/Healthie.NET).

State is written on **every tick of every checker**. A relational provider does a round trip to a disk-backed engine for each of those; this does one to memory. That is the whole reason to pick it.

## Installation

```shell
dotnet add package Healthie.NET.Redis
```

## Usage

```csharp
using Healthie.StateProviding.Redis;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieRedis("localhost:6379");
```

If your application already registers an `IConnectionMultiplexer` — for its own cache, or through `AddStackExchangeRedisCache` — share it instead of opening a second connection to the same server:

```csharp
builder.Services.AddHealthieRedis();     // uses the registered IConnectionMultiplexer
```

Both shapes are the same method: pass a configuration string to have the connection opened for you, or leave it out to use the registered one. Name the argument when you only want a different prefix — `AddHealthieRedis(keyPrefix: "myapp:health:")`.

Every key is prefixed, `healthie:state:` by default, so the provider stays out of the way of whatever else lives on that server. Pass your own as the last argument.

## Optimistic concurrency

`SupportsOptimisticConcurrency` is `true`. A write can be made conditional on the state not having changed since it was read, which is what stops a check overwriting a setting somebody changed from the dashboard.

The compare and the write are a **Lua script**, which Redis runs to completion without interleaving anything else:

```lua
if redis.call('HGET', KEYS[1], 'version') ~= ARGV[3] then
    return 0
end
redis.call('HSET', KEYS[1], 'value', ARGV[1], 'state_type', ARGV[2], 'version', ARGV[4])
return 1
```

A `WATCH`/`MULTI`/`EXEC` transaction would also work, but it needs a retry loop of its own around the optimistic failure, and it holds state on the connection. A script needs neither.

Creating is the same shape against `EXISTS`, so two writers that both find nothing cannot both create — the case where there is no version to compare and a lost update would otherwise slip through.

## How it works

- One **hash** per checker, at `{prefix}{checkerName}`, holding `value` (the state as JSON), `state_type`, and `version`. A hash rather than a plain string so the version can be swapped in the same command as the write.
- `GetStatesAsync` issues every read before awaiting any. StackExchange.Redis pipelines on one connection, so listing every checker on the dashboard costs one round trip rather than one per checker.
- Each hash records the type its state was written as, and reading it as a different type throws rather than returning a mismatched state. The comparison is on `Type.FullName`, not the assembly-qualified name — that embeds the assembly version, and this library's version changes with every release, so comparing it would make state written by one release unreadable by the next.
- A hash written before this provider versioned its writes has no `version` field. It reports as unversioned, and a caller writes it unconditionally exactly as it did before; the next write gives it one.

## Durability is Redis's, not this package's

Redis is in memory. Whether state survives a restart is your Redis configuration — RDB snapshots, AOF, or a managed offering's own guarantees — not something this provider can promise. For pulse checker state that is usually the right trade: the interesting state is the current one, and a lost history is a gap in a chart rather than an outage. If it is not the right trade for you, use PostgreSQL, SQL Server or CosmosDB.

## See also

- [Healthie.NET](https://www.nuget.org/packages/Healthie.NET) — the metapackage
- [Healthie.NET.Postgres](https://www.nuget.org/packages/Healthie.NET.Postgres) / [Healthie.NET.SqlServer](https://www.nuget.org/packages/Healthie.NET.SqlServer) / [Healthie.NET.CosmosDb](https://www.nuget.org/packages/Healthie.NET.CosmosDb) — the durable-by-default alternatives
