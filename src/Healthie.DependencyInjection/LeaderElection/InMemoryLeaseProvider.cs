using System.Collections.Concurrent;

namespace Healthie.LeaderElection;

/// <summary>
/// Holds leases in memory, which makes the holder the leader of itself.
/// </summary>
/// <remarks>
/// The default so that leader election can be switched on and tested without standing anything up.
/// It is useless for its actual purpose: nothing is shared between processes, so every replica
/// wins its own lease and every replica runs every check -- exactly the situation leader election
/// exists to fix. Register a shared provider before deploying more than one replica.
/// </remarks>
public sealed class InMemoryLeaseProvider : ILeaseProvider
{
    private readonly ConcurrentDictionary<string, (string HolderId, DateTime ExpiresAt)> _leases = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(
        string leaseName,
        string holderId,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(holderId);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A lease duration must be positive.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            if (duration > DateTime.MaxValue - now)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "The lease expiry would be later than DateTime.MaxValue.");
            }

            var replacement = (holderId, ExpiresAt: now + duration);

            if (!_leases.TryGetValue(leaseName, out var current))
            {
                if (_leases.TryAdd(leaseName, replacement))
                {
                    return Task.FromResult(true);
                }

                continue;
            }

            // Free if it has expired, and always renewable by whoever already holds it. The
            // compare-and-swap makes the returned answer describe the write that actually landed,
            // rather than a ConcurrentDictionary factory that may have been retried.
            if (current.ExpiresAt > now && current.HolderId != holderId)
            {
                return Task.FromResult(false);
            }

            if (_leases.TryUpdate(leaseName, replacement, current))
            {
                return Task.FromResult(true);
            }
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string leaseName, string holderId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        // Only the holder may release: a replica that lost the lease and then shut down must not
        // take it away from whoever has it now.
        if (_leases.TryGetValue(leaseName, out var current) && current.HolderId == holderId)
        {
            _leases.TryRemove(new KeyValuePair<string, (string, DateTime)>(leaseName, current));
        }

        return Task.CompletedTask;
    }
}
