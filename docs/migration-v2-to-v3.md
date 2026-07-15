# Migrating from v2.x to v3.0

This guide covers the breaking changes introduced in v3.0. If you are starting fresh, you do not
need it -- see the [README](../README.md) instead.

Most applications need no code changes at all. The breaks are in interfaces you only touch if you
implemented them yourself, and in behaviour that was arguably wrong before.

## Breaking Changes

| Area | v2.x | v3.0 | Action Required |
|---|---|---|---|
| `IPulseChecker` | 12 members | Adds `SetTagsAsync`, `SetPinnedAsync`, `SetGroupAsync` | Only if you implement the interface directly. Deriving from `PulseChecker` needs nothing. |
| `IPulsesScheduler` | -- | Adds `SetTagsAsync`, `SetPinnedAsync`, `SetGroupAsync` | Only if you wrote your own scheduler. |
| `IStateProvider` default | None registered; resolving a checker threw | `InMemoryStateProvider` registered by `AddHealthie()` | None. A missing provider used to be a runtime failure and is now a working default. Registering your own still wins. |
| Provider registration | `AddSingleton` -- last call won | `TryAddSingleton` -- your registration wins over the built-in default | None, unless you relied on call order to override. `AddHealthieQuartz()` / `AddHealthieCosmosDb()` now win regardless of order. |
| `Newtonsoft.Json` | A dependency of `Healthie.NET.Abstractions` | Removed | None, unless you depended on it transitively. State written by v2 still reads back. |
| API: unknown checker | Threw, and the message was matched on | Returns `404` | None, unless you parsed the exception text. |
| Target frameworks | `net8.0` | `net8.0;net10.0` | None. |
| `PulseChecker.Name` | Fixed to the type name | `virtual` | None. Override it when one type is registered more than once. |

## Behaviour changes worth knowing

**A failure to reach the state store is no longer recorded as a failed check.** Previously, if
Healthie could not read or write its own state, that surfaced as the monitored component being
unhealthy -- reporting a perfectly healthy database as down because CosmosDB was unreachable. Only
the monitored component's own failures become health results now; a state store failure surfaces as
an exception. Cancelling a check no longer records a failure either.

**The dashboard shows UTC.** It renders on the server, so the "local" time it used to print was the
server's, shown to a viewer who might be nowhere near it. Every time it shows is now UTC and says
so.

**`MaxHistoryLength` accepts up to 100**, up from 10. The default is still 10.

## New: groups and tags

Not a break -- there is nothing to migrate -- but this is the main addition, and the two concepts
are easy to confuse.

A checker belongs to **one group at most**. Groups are what the dashboard's sectioned view splits
on, which is what makes it a partition: every checker appears once, under exactly one heading, and a
section's tallies add up to what is under it.

A checker carries **any number of tags**. Tags describe it and filter the list, and are free to cut
across groups -- a `tier-1` tag can sit on checkers in three different groups.

```csharp
public class RedisCachePulseChecker(IStateProvider stateProvider) : PulseChecker(stateProvider)
{
    public override string DisplayName => "Redis Cache";

    // Where it lives. One, or none.
    public override string DefaultGroup => "Data Stores";

    // What it is. Any number, and they may overlap with other groups.
    public override IReadOnlyList<string> DefaultTags => ["tier-1", "cache"];

    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => /* ... */;
}
```

Both are seeded into `PulseCheckerState` the first time a checker runs and are never applied again,
so a change made on the dashboard -- or through `SetGroupAsync` / `SetTagsAsync` -- survives a
restart instead of being reset to whatever the code says.

`SetPinnedAsync` sorts a checker above the others. A pin is stored with the checker rather than per
viewer: the checks worth watching are the same for everyone.

## Existing state

State written by v2.x reads back unchanged. `Tags` and `Group` are absent from those documents and
deserialize to empty and `null`, which means a checker carrying stored state from v2 starts
untagged and ungrouped rather than picking up the defaults its code declares. Set them from the
dashboard, or clear the checker's stored state to have the defaults seed it again.

CosmosDB documents written up to 2.3.0 recorded an assembly-qualified type name; 3.0 records the
name without the assembly version and compares ignoring it, so both are readable.
