using Temporalio.Workflows;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// The workflow Temporal starts on each occurrence of a checker's schedule.
/// </summary>
/// <remarks>
/// It does nothing but call the activity. Workflow code must be deterministic -- no clocks, no
/// network, no random -- and a pulse check is none of those things, so all the real work belongs on
/// the activity side of that boundary.
/// </remarks>
[Workflow]
public sealed class PulseCheckerWorkflow
{
    /// <summary>Triggers the named pulse checker.</summary>
    /// <param name="checkerName">The checker to trigger.</param>
    [WorkflowRun]
    public Task RunAsync(string checkerName) =>
        Workflow.ExecuteActivityAsync(
            (PulseCheckerActivities activities) => activities.TriggerAsync(checkerName),
            new()
            {
                // Generous, because the timeout that matters is the check's own. A check outliving
                // this is a component so slow it may as well be down.
                StartToCloseTimeout = TimeSpan.FromMinutes(5),
            });
}
