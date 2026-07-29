![Healthie.NET - Trust your uptime](https://raw.githubusercontent.com/ivanvyd/Healthie.NET/main/healthie.net.banner.png)

# Healthie.NET.LeaderElection

[![NuGet](https://img.shields.io/nuget/v/Healthie.NET.LeaderElection.svg)](https://www.nuget.org/packages/Healthie.NET.LeaderElection)

**▶ [Live demo — healthie.compiletheory.com](https://healthie.compiletheory.com)** — a read-only Healthie.NET dashboard watching real status pages (Anthropic, OpenAI, GitHub, Cloudflare, and more), built from these packages.

Runs pulse checks on one replica at a time.

## The problem it solves

Without it, **every replica runs every check**. Three replicas mean:

- a database asked three times whether it is healthy, on every interval
- three sets of results racing to write the same state document, last write winning
- one outage paging somebody three times

None of that is visible from a dashboard, which is what makes it worth fixing before it matters.

## Installation

```shell
dotnet add package Healthie.NET.LeaderElection
```

## Usage

```csharp
using Healthie.LeaderElection;

builder.Services
    .AddHealthie(typeof(Program).Assembly)
    .AddHealthieLeaderElection();          // after the scheduler it should wrap

builder.Services.AddSingleton<ILeaseProvider, YourSharedLeaseProvider>();
```

**Call it after the scheduler.** It decorates whatever `IPulseScheduler` is registered at that point, so unlike every other `AddHealthie*` in this library it is *not* order-independent. Calling it first throws with an explanation rather than silently wrapping the built-in timer when you meant Quartz.

It works with every scheduler — the built-in timer, Quartz, Hangfire, Coravel, Temporal — because it wraps rather than replaces.

## You need a shared lease provider

The default keeps leases **in memory**, which makes every replica the leader of itself and leaves the problem exactly where it was. It exists so the feature can be switched on and tested without standing anything up.

Implement `ILeaseProvider` against something your replicas share — a table with a conditional update, a Redis `SET NX`, a blob lease. It is two methods:

```csharp
Task<bool> TryAcquireAsync(string leaseName, string holderId, TimeSpan duration, CancellationToken ct);
Task ReleaseAsync(string leaseName, string holderId, CancellationToken ct);
```

Acquire and renew are one operation on purpose — take it if nobody holds it, if it has expired, or if it is already mine. Separating them invites a renew that succeeds against a lease somebody else now holds.

## Failover

A lease **expires** rather than being handed over, because the failure worth designing for is the replica that stops without saying anything — killed, redeployed, partitioned away. Another replica takes over once the lease lapses, without needing its cooperation.

`LeaseDuration` (30s default) is therefore how long checks pause when a leader dies abruptly. `RenewInterval` (10s) is comfortably shorter, so a single slow round trip does not move leadership for no reason.

If the lease store becomes unreachable, the replica **stands down**. It can no longer prove it leads, and two leaders is the state this exists to prevent.
