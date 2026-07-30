using Healthie.Abstractions.Insights;

namespace Healthie.LeaderElection;

/// <summary>
/// Tells the dashboard whether this replica is the one running the checks.
/// </summary>
/// <remarks>
/// Without it, a board served by a follower shows every checker sitting still and reads as broken:
/// nothing is running here, and nothing is meant to be. The replica id is there because an operator
/// comparing two tabs needs to know which is which.
/// </remarks>
/// <param name="scheduler">The scheduler that holds, or does not hold, the lease.</param>
/// <param name="options">Where the replica's own identity comes from.</param>
internal sealed class LeadershipInsights(
    LeaderElectedPulseScheduler scheduler,
    LeaderElectionOptions options) : ILeadershipInsights
{
    /// <inheritdoc />
    public bool IsLeader => scheduler.IsLeader;

    /// <inheritdoc />
    public string ReplicaId => options.HolderId;
}
