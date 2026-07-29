using Healthie.Abstractions;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// The activity that actually runs a pulse check.
/// </summary>
/// <remarks>
/// Resolved from dependency injection by the worker, and given the checker's <em>name</em> rather
/// than the checker. A workflow's arguments are serialized into Temporal's history and read back by
/// whichever worker picks the task up, which a checker -- with its state provider and its semaphore
/// -- does not survive.
/// </remarks>
public sealed class PulseCheckerActivities(
    IEnumerable<IPulseChecker> pulseCheckers,
    ILogger<PulseCheckerActivities>? logger = null)
{
    /// <summary>Triggers the named pulse checker.</summary>
    /// <param name="checkerName">The <see cref="IPulseChecker.Name"/> of the checker to trigger.</param>
    [Activity]
    public async Task TriggerAsync(string checkerName)
    {
        var checker = pulseCheckers.FirstOrDefault(c => c.Name == checkerName);

        // A Temporal schedule outlives the code that created it, so the cluster can still hold one
        // for a checker that has since been renamed or removed. Saying so is more use than throwing,
        // which Temporal would retry on a schedule of its own.
        if (checker is null)
        {
            logger?.LogError(
                "Pulse checker '{CheckerName}' is not registered; its Temporal schedule is stale. Delete it, " +
                "and its leftover state with IStateProvider.DeleteStateAsync.",
                checkerName);
            return;
        }

        await checker.TriggerAsync(ActivityExecutionContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
