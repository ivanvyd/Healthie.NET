using Healthie.Abstractions;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Healthie.Scheduling.Quartz.Jobs;

/// <summary>
/// A Quartz <see cref="IJob"/> that triggers a pulse checker identified by name.
/// The checker is resolved from the collection of registered <see cref="IPulseChecker"/>
/// instances via dependency injection.
/// </summary>
/// <remarks>
/// The <see cref="DisallowConcurrentExecutionAttribute"/> prevents overlapping executions
/// of the same pulse checker job, ensuring thread-safe state updates.
/// </remarks>
[DisallowConcurrentExecution]
public sealed class PulseCheckerJob(
    IEnumerable<IPulseChecker> pulseCheckers,
    ILogger<PulseCheckerJob>? logger = null) : IJob
{
    /// <summary>
    /// The key used to store and retrieve the pulse checker name from the Quartz <see cref="JobDataMap"/>.
    /// </summary>
    public static readonly string CheckerNameKey = "checkerName";

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var checkerName = context.MergedJobDataMap.GetString(CheckerNameKey);

        if (string.IsNullOrEmpty(checkerName))
        {
            logger?.LogError("Pulse checker job executed without a '{Key}' in the job data map.", CheckerNameKey);
            return;
        }

        var checker = pulseCheckers.FirstOrDefault(c => c.Name == checkerName);

        if (checker is null)
        {
            logger?.LogError(
                "Pulse checker '{CheckerName}' not found in registered checkers.",
                checkerName);
            return;
        }

        try
        {
            await checker.TriggerAsync(context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger?.LogInformation(
                "Pulse checker '{CheckerName}' execution was cancelled.",
                checkerName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Error triggering pulse checker '{CheckerName}'.",
                checkerName);

            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
