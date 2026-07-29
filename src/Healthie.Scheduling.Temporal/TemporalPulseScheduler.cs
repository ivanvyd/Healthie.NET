using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Api.Enums.V1;
using Temporalio.Client.Schedules;
using Temporalio.Exceptions;

namespace Healthie.Scheduling.Temporal;

/// <summary>
/// An <see cref="IPulseScheduler"/> backed by Temporal schedules.
/// </summary>
/// <remarks>
/// <para>
/// What Temporal adds over the built-in timer is that the schedule lives in the cluster rather than
/// in the process. It survives a restart and a redeploy, each occurrence is handed to exactly one
/// worker however many replicas are running, and every run has a history somebody can look at.
/// </para>
/// <para>
/// The cost is a Temporal cluster. This is worth it when one is already there; if it is not, the
/// built-in timer or Hangfire will be a better trade -- Hangfire gives the same
/// survives-a-restart, runs-once-across-replicas properties against a database you probably
/// already run.
/// </para>
/// </remarks>
/// <param name="client">A connected Temporal client. The application owns its lifetime and options.</param>
/// <param name="options">The task queue and schedule naming.</param>
/// <param name="logger">An optional logger for diagnostic output.</param>
public sealed class TemporalPulseScheduler(
    ITemporalClient client,
    HealthieTemporalOptions options,
    ILogger<TemporalPulseScheduler>? logger = null) : IPulseScheduler
{
    private readonly ITemporalClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly HealthieTemporalOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task ScheduleAsync(
        IPulseChecker checker,
        PulseInterval interval,
        CancellationToken cancellationToken = default) =>
        ScheduleAsync(checker, PulseSchedule.FromInterval(interval), cancellationToken);

    /// <inheritdoc />
    public async Task ScheduleAsync(
        IPulseChecker checker,
        PulseSchedule schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(schedule);

        var scheduleId = ScheduleId(checker);

        // Temporal has no create-or-replace, so an existing schedule is removed first. Deleting
        // something that is not there is not an error, which is what makes this safe to repeat.
        await UnscheduleAsync(checker, cancellationToken).ConfigureAwait(false);

        await _client.CreateScheduleAsync(
            scheduleId,
            new Schedule(
                Action: ScheduleActionStartWorkflow.Create(
                    (PulseCheckerWorkflow workflow) => workflow.RunAsync(checker.Name),
                    new(id: $"{scheduleId}-run", taskQueue: _options.TaskQueue)),
                Spec: TemporalScheduleSpec.From(schedule))
            {
                Policy = new()
                {
                    // Skip rather than buffer. A checker already refuses to run on top of itself, so
                    // a backlog would only queue occurrences that each return immediately -- and a
                    // check that is late is worth less than the next one, which is current.
                    Overlap = ScheduleOverlapPolicy.Skip,
                },
            },
            new ScheduleOptions { Rpc = new() { CancellationToken = cancellationToken } })
            .ConfigureAwait(false);

        logger?.LogInformation(
            "Scheduled pulse checker '{CheckerName}' as Temporal schedule '{ScheduleId}' on {Schedule}.",
            checker.Name,
            scheduleId,
            schedule);
    }

    /// <inheritdoc />
    public async Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checker);

        try
        {
            await _client
                .GetScheduleHandle(ScheduleId(checker))
                .DeleteAsync(new RpcOptions { CancellationToken = cancellationToken })
                .ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            // Nothing to remove. Unscheduling something that was never scheduled is a no-op
            // everywhere else in this library, and callers rely on it before rescheduling.
        }
    }

    /// <summary>The Temporal schedule identifier for a checker.</summary>
    /// <remarks>
    /// Prefixed so Healthie's schedules are recognisable in the Temporal UI, and cannot collide with
    /// an application's own schedule of the same name.
    /// </remarks>
    internal string ScheduleId(IPulseChecker checker) => $"{_options.SchedulePrefix}{checker.Name}";
}
