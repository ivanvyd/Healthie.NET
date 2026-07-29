using Hangfire;
using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.Scheduling.Hangfire.Jobs;
using Microsoft.Extensions.Logging;

namespace Healthie.Scheduling.Hangfire;

/// <summary>
/// An <see cref="IPulseScheduler"/> backed by Hangfire recurring jobs.
/// </summary>
/// <remarks>
/// <para>
/// What Hangfire adds over the built-in timer is that the schedule lives in storage rather than in
/// the process. It survives a restart, and across several replicas each occurrence is handed to
/// exactly one server -- so a scaled-out deployment runs each check once rather than once per
/// replica.
/// </para>
/// <para>
/// The cost is granularity. Hangfire notices due work by polling, every fifteen seconds by default,
/// so a check asking to run more often than that will not. Lower
/// <c>BackgroundJobServerOptions.SchedulePollingInterval</c> if a shorter period matters, or keep
/// those checks on the built-in timer.
/// </para>
/// </remarks>
/// <param name="recurringJobs">Hangfire's recurring job manager.</param>
/// <param name="logger">An optional logger for diagnostic output.</param>
public sealed class HangfirePulseScheduler(
    IRecurringJobManager recurringJobs,
    ILogger<HangfirePulseScheduler>? logger = null) : IPulseScheduler
{
    private readonly IRecurringJobManager _recurringJobs = recurringJobs
        ?? throw new ArgumentNullException(nameof(recurringJobs));

    /// <inheritdoc />
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default) =>
        ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">
    /// No cron expression fires at exactly this period. See <see cref="PeriodCron"/>.
    /// </exception>
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        // Converted before anything is written, so a schedule Hangfire cannot express leaves the
        // recurring job that is already there running.
        var cron = PeriodCron.From(schedule, checker.Name);

        _recurringJobs.AddOrUpdate<PulseCheckerJob>(
            RecurringJobId(checker),
            job => job.ExecuteAsync(checker.Name, CancellationToken.None),
            cron);

        logger?.LogInformation(
            "Scheduled pulse checker '{CheckerName}' as a Hangfire recurring job on '{Cron}'.",
            checker.Name,
            cron);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        _recurringJobs.RemoveIfExists(RecurringJobId(checker));

        logger?.LogInformation("Unscheduled pulse checker '{CheckerName}'.", checker.Name);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The recurring job identifier for a checker.
    /// </summary>
    /// <remarks>
    /// Prefixed so that Healthie's jobs are recognisable in the Hangfire dashboard, and cannot
    /// collide with an application's own recurring job of the same name.
    /// </remarks>
    internal static string RecurringJobId(IPulseChecker checker) => $"healthie:{checker.Name}";
}
