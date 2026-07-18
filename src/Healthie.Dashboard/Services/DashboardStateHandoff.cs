using Healthie.Abstractions.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Healthie.Dashboard.Services;

/// <summary>
/// Carries the checker states the dashboard read while prerendering across to its interactive
/// render, on the server, so the board itself never travels to the browser and back.
/// </summary>
/// <remarks>
/// The interactive render needs the same states the prerender drew, or it starts from an empty board
/// and briefly replaces the one already on screen -- the load flicker. The framework's own way to
/// carry state across, persisting it into the page, would put the whole board on the SignalR circuit:
/// that grows with every checker and, past the 32&#160;KB default message limit, is rejected and the
/// circuit is dropped outright. Blazor Server already holds the states on the server, so this keeps
/// them there. Prerendering stashes them under a short-lived token, the page carries only the token,
/// and the interactive render collects them straight back. The wire cost is one token, whatever the
/// size of the board.
/// </remarks>
internal sealed class DashboardStateHandoff : IDisposable
{
    /// <summary>
    /// How long a stashed snapshot is kept -- long enough to bridge a prerender to the circuit
    /// connecting (a second or two in practice), short enough that a page closed before it connects
    /// does not linger.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A ceiling on how many handoffs are held at once. It only bounds abandoned entries, which
    /// expire on their own anyway; past it the oldest are evicted and those renders fall back to
    /// reading the provider -- correct, just not free.
    /// </summary>
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });

    /// <summary>Stashes a snapshot and returns the token that collects it.</summary>
    public string Stash(DashboardSnapshot snapshot)
    {
        var token = Guid.NewGuid().ToString("N");

        _cache.Set(token, snapshot, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1,
        });

        return token;
    }

    /// <summary>Collects a stashed snapshot, once, if it is still held.</summary>
    public bool TryCollect(string token, out DashboardSnapshot? snapshot)
    {
        if (_cache.TryGetValue(token, out snapshot) && snapshot is not null)
        {
            // One handoff, one read: drop it so a replayed token cannot pick up a stale board.
            _cache.Remove(token);
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Dispose() => _cache.Dispose();
}

/// <summary>The states handed from a dashboard's prerender to its interactive render.</summary>
/// <remarks>
/// Only what the first render needs. The event log and the clock are per-circuit commentary that
/// starts fresh on every load, so neither is carried across.
/// </remarks>
internal sealed record DashboardSnapshot(
    Dictionary<string, PulseCheckerState> States,
    Dictionary<string, string> DisplayNames);
