namespace Healthie.Abstractions.Insights;

/// <summary>
/// Whether this replica is the one running the checks.
/// </summary>
/// <remarks>
/// With leader election on, only one replica runs a checker on each interval. A board that did not
/// say so would show a follower with everything idle and look broken. Declared here rather than in
/// the leader-election package -- see <see cref="IUptimeInsights"/> for why. Reads only.
/// </remarks>
public interface ILeadershipInsights
{
    /// <summary>Whether this replica currently holds the lease.</summary>
    bool IsLeader { get; }

    /// <summary>Something identifying this replica, for a board an operator is comparing across tabs.</summary>
    string ReplicaId { get; }
}
