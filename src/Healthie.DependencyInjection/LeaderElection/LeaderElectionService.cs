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
    private readonly LeaderElectionOptions _options = Validate(options);
    private CancellationTokenSource? _leadershipCancellation;
    private Task? _reconciliation;

    private static LeaderElectionOptions Validate(LeaderElectionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LeaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HolderId);

        var renewMilliseconds = options.RenewInterval.TotalMilliseconds;
        if (renewMilliseconds < 1 || renewMilliseconds >= uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.RenewInterval,
                $"{nameof(LeaderElectionOptions.RenewInterval)} must be from one millisecond through " +
                $"{uint.MaxValue - 1:N0} milliseconds.");
        }

        if (options.LeaseDuration <= options.RenewInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.LeaseDuration,
                $"{nameof(LeaderElectionOptions.LeaseDuration)} must be longer than " +
                $"{nameof(LeaderElectionOptions.RenewInterval)} so the lease is renewed before it expires.");
        }

        return options;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RenewInterval);

        try
        {
            do
            {
                await ContendAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown may interrupt lease acquisition or reconciliation, not only the timer wait.
        }
        finally
        {
            _leadershipCancellation?.Cancel();
            await StandDownOnShutdownAsync().ConfigureAwait(false);
        }
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
                StartReconciliation(stoppingToken);
            }
            else
            {
                await StopLeadershipAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The store being unreachable is not a reason to keep running checks: this replica can
            // no longer prove it is the leader, and two leaders is the state this exists to prevent.
            logger?.LogError(ex, "Could not contend for the '{LeaseName}' lease; standing down.", _options.LeaseName);

            try
            {
                await StopLeadershipAsync(stoppingToken).ConfigureAwait(false);
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
        var stoodDown = false;
        try
        {
            using var standDown = new CancellationTokenSource(_options.RenewInterval);
            await _scheduler.StandDownAsync(standDown.Token).ConfigureAwait(false);
            stoodDown = true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not stop every pulse checker on shutdown.");
        }

        // Releasing after partial cleanup would let another replica start while a failed schedule
        // still runs here. Keep the lease until it expires instead.
        if (!stoodDown)
        {
            return;
        }

        try
        {
            // Safe for followers: providers only release a lease held by this replica's HolderId.
            using var release = new CancellationTokenSource(_options.RenewInterval);
            await _leases
                .ReleaseAsync(_options.LeaseName, _options.HolderId, release.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "Could not release the '{LeaseName}' lease on shutdown; it will expire.",
                _options.LeaseName);
        }
    }

    /// <summary>
    /// Starts one reconciliation without putting lease renewal behind remote state or scheduler
    /// calls. A later lease tick starts the next reconciliation after this one finishes.
    /// </summary>
    private void StartReconciliation(CancellationToken stoppingToken)
    {
        if (_reconciliation is { IsCompleted: false })
        {
            return;
        }

        _leadershipCancellation?.Dispose();
        _leadershipCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _reconciliation = ReconcileAsLeaderAsync(_leadershipCancellation.Token);
    }

    private async Task ReconcileAsLeaderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _scheduler.BecomeLeaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Losing the lease or shutting down interrupts unfinished reconciliation.
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Could not reconcile pulse schedules while leading; standing down.");

            try
            {
                await _scheduler.StandDownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception standDownFailure) when (standDownFailure is not OperationCanceledException)
            {
                logger?.LogError(standDownFailure, "Could not stand down after reconciliation failed.");
            }
        }
    }

    private async Task StopLeadershipAsync(CancellationToken cancellationToken)
    {
        _leadershipCancellation?.Cancel();
        await _scheduler.StandDownAsync(cancellationToken).ConfigureAwait(false);
    }
}
