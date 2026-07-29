namespace Healthie.Scheduling.Temporal;

/// <summary>
/// How Healthie's Temporal schedules are named and where their work runs.
/// </summary>
public sealed class HealthieTemporalOptions
{
    /// <summary>
    /// The task queue the check workflows run on. Defaults to <c>healthie</c>.
    /// </summary>
    /// <remarks>
    /// A worker must be listening on it, registered with <see cref="PulseCheckerWorkflow"/> and
    /// <see cref="PulseCheckerActivities"/>. Without one the schedules fire and the workflows sit
    /// unstarted, which looks exactly like checks that stopped running.
    /// </remarks>
    public string TaskQueue { get; set; } = "healthie";

    /// <summary>
    /// Prefixed to every schedule identifier. Defaults to <c>healthie-</c>.
    /// </summary>
    /// <remarks>
    /// So Healthie's schedules are recognisable in the Temporal UI, and cannot collide with an
    /// application's own schedule that happens to share a checker's name.
    /// </remarks>
    public string SchedulePrefix { get; set; } = "healthie-";
}
