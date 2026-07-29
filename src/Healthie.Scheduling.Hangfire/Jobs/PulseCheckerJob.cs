using Healthie.Abstractions;
using Microsoft.Extensions.Logging;

namespace Healthie.Scheduling.Hangfire.Jobs;

/// <summary>
/// The job Hangfire runs on each occurrence of a pulse checker's schedule.
/// </summary>
/// <remarks>
/// Hangfire stores the checker's <em>name</em> and this resolves the instance from DI, rather than
/// the recurring job holding the checker itself. A recurring job is serialized into storage and
/// outlives the process that created it, so anything it captures has to survive being written down
/// and read back by a different process -- which a checker, with its state provider and its
/// semaphore, does not.
/// </remarks>
public sealed class PulseCheckerJob(
    IEnumerable<IPulseChecker> pulseCheckers,
    ILogger<PulseCheckerJob>? logger = null)
{
    /// <summary>
    /// Triggers the named pulse checker.
    /// </summary>
    /// <param name="checkerName">The <see cref="IPulseChecker.Name"/> of the checker to trigger.</param>
    /// <param name="cancellationToken">Supplied by Hangfire; signalled when the server shuts down.</param>
    public async Task ExecuteAsync(string checkerName, CancellationToken cancellationToken)
    {
        var checker = pulseCheckers.FirstOrDefault(c => c.Name == checkerName);

        // A recurring job outlives the code that registered it, so storage can still hold one for a
        // checker that has since been renamed or removed. Saying so is more use than throwing, which
        // Hangfire would retry on a schedule of its own.
        if (checker is null)
        {
            logger?.LogError(
                "Pulse checker '{CheckerName}' is not registered; its Hangfire recurring job is stale " +
                "and can be removed.",
                checkerName);
            return;
        }

        try
        {
            await checker.TriggerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger?.LogInformation("Pulse checker '{CheckerName}' was cancelled.", checkerName);
        }
        catch (Exception ex)
        {
            // Rethrown so the run is recorded as failed and Hangfire's retry policy applies -- a
            // check that threw is a component that failed, and it is worth another look sooner than
            // its next occurrence.
            logger?.LogError(ex, "Error triggering pulse checker '{CheckerName}'.", checkerName);
            throw;
        }
    }
}
