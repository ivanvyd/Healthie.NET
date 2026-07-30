namespace Healthie.LeaderElection;

/// <summary>
/// Which lease decides leadership, and how long it is good for.
/// </summary>
public sealed class LeaderElectionOptions
{
    /// <summary>The lease every replica contends for. Defaults to <c>healthie-scheduler</c>.</summary>
    /// <remarks>
    /// Change it to run two independent Healthie deployments against one store without them
    /// competing for the same leadership.
    /// </remarks>
    public string LeaseName { get; set; } = "healthie-scheduler";

    /// <summary>
    /// Identifies this replica. Defaults to the machine name and process id.
    /// </summary>
    /// <remarks>
    /// It has to differ between replicas and stay the same for the life of one, which the default
    /// satisfies -- two pods have different names, and a process keeps its id. Set it explicitly if
    /// your replicas can share a machine name.
    /// </remarks>
    public string HolderId { get; set; } = $"{Environment.MachineName}:{Environment.ProcessId}";

    /// <summary>How long a lease is good for once taken. Defaults to thirty seconds.</summary>
    /// <remarks>
    /// This is how long checks stop for when a leader dies without releasing: no other replica may
    /// take over until it expires. Shorter means faster failover and more load on the store.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often to renew or contend. Defaults to ten seconds.</summary>
    /// <remarks>
    /// Comfortably shorter than the duration on purpose. A leader that renewed only as often as the
    /// lease lasted would lose it to a single slow round trip, and the checks would move between
    /// replicas for no reason.
    /// </remarks>
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(10);
}
