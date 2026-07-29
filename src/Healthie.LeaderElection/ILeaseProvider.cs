namespace Healthie.LeaderElection;

/// <summary>
/// A lock one replica holds at a time, so that only one of them runs the checks.
/// </summary>
/// <remarks>
/// Deliberately small. Everything a leader election needs is "can I hold this name for a while,
/// and can I keep holding it" -- and any store with a conditional write can answer that, which is
/// why this is an interface rather than a database.
/// <para>
/// A lease has a expiry rather than being released on shutdown, because the interesting failure is
/// the replica that stops without releasing anything: the process was killed, the node went away,
/// the network partitioned. Another replica has to be able to take over without its cooperation.
/// </para>
/// </remarks>
public interface ILeaseProvider
{
    /// <summary>
    /// Takes the lease, or renews it if this holder already has it.
    /// </summary>
    /// <param name="leaseName">The lease being contended for.</param>
    /// <param name="holderId">Identifies this replica.</param>
    /// <param name="duration">How long the lease is good for if taken.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns><c>true</c> if this holder now holds the lease; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Acquire and renew are one operation on purpose. They are the same conditional write -- take
    /// it if nobody holds it, if it has expired, or if it is already mine -- and separating them
    /// would invite a renew that silently succeeds against a lease somebody else has taken.
    /// </remarks>
    Task<bool> TryAcquireAsync(
        string leaseName,
        string holderId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives up the lease, if this holder has it.
    /// </summary>
    /// <param name="leaseName">The lease to release.</param>
    /// <param name="holderId">Identifies this replica.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <remarks>
    /// A courtesy for an orderly shutdown, so the next replica takes over at once rather than
    /// waiting out the expiry. Correctness never depends on it being called.
    /// </remarks>
    Task ReleaseAsync(string leaseName, string holderId, CancellationToken cancellationToken = default);
}
