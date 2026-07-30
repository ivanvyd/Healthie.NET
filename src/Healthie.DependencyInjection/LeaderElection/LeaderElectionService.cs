using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthie.LeaderElection;

/// <summary>
/// Contends for the lease, and tells the scheduler when this replica wins or loses it.
/// </summary>
/// <remarks>
/// Every replica runs this and every replica keeps trying, so the one holding the lease keeps
/// renewing it while the others wait. When a leader stops -- killed, redeployed, partitioned away
/// -- its lease expires and the next replica to try takes over without needing to hear from it.
/// </remarks>
/// <param name="leases">Where the lease lives.</param>
/// <param name="scheduler">The scheduler to start and stop.</param>
/// <param name="options">Lease name, duration and how often to try.</param>
/// <param name="logger">An optional logger for diagnostic output.</param>
public sealed class LeaderElectionService(
    ILeaseProvider leases,
    LeaderElectedPulseScheduler scheduler,
    LeaderElectionOptions options,
    ILogger<LeaderElectionService>? logger = null) : BackgroundService
{
    private readonly ILeaseProvider _leases = leases ?? throw new ArgumentNullException(nameof(leases));
    private readonly LeaderElectedPulseScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    private readonly LeaderElectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RenewInterval);

        do
        {
            await ContendAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));

        await StandDownOnShutdownAsync().ConfigureAwait(false);
    }

    private async Task ContendAsync(CancellationToken stoppingToken)
    {
        try
        {
            var won = await _leases
                .TryAcquireAsync(_options.LeaseName, _options.HolderId, _options.LeaseDuration, stoppingToken)
                .ConfigureAwait(false);

            if (won)
            {
                await _scheduler.BecomeLeaderAsync(stoppingToken).ConfigureAwait(false);
            }
            else
            {
                await _scheduler.StandDownAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The store being unreachable is not a reason to keep running checks: this replica can
            // no longer prove it is the leader, and two leaders is the state this exists to prevent.
            logger?.LogError(ex, "Could not contend for the '{LeaseName}' lease; standing down.", _options.LeaseName);

            try
            {
                await _scheduler.StandDownAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception standDownFailure) when (standDownFailure is not OperationCanceledException)
            {
                logger?.LogError(standDownFailure, "Could not stand down after failing to contend.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the lease on the way out, so the next replica takes over at once.
    /// </summary>
    /// <remarks>
    /// Uses its own token rather than the stopping one, which is already cancelled by this point.
    /// A best-effort courtesy: correctness rests on the lease expiring, not on this running.
    /// </remarks>
    private async Task StandDownOnShutdownAsync()
    {
        if (!_scheduler.IsLeader)
        {
            return;
        }

        using var shutdown = new CancellationTokenSource(_options.RenewInterval);

        try
        {
            await _scheduler.StandDownAsync(shutdown.Token).ConfigureAwait(false);
            await _leases.ReleaseAsync(_options.LeaseName, _options.HolderId, shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not release the '{LeaseName}' lease on shutdown; it will expire.", _options.LeaseName);
        }
    }
}
