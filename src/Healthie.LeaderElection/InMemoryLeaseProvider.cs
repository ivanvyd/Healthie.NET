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

        var now = DateTime.UtcNow;
        var taken = false;

        _leases.AddOrUpdate(
            leaseName,
            _ =>
            {
                taken = true;
                return (holderId, now + duration);
            },
            (_, current) =>
            {
                // Free if it has expired, and always renewable by whoever already holds it.
                if (current.ExpiresAt <= now || current.HolderId == holderId)
                {
                    taken = true;
                    return (holderId, now + duration);
                }

                return current;
            });

        return Task.FromResult(taken);
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
