using Healthie.Abstractions;
using Healthie.Abstractions.Scheduling;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Healthie.Mcp;

/// <summary>
/// The tools that change a checker's state.
/// </summary>
/// <remarks>
/// These are only registered when <see cref="HealthieMcpOptions.AllowMutations"/> is turned on, so a
/// server is read-only unless it has been deliberately opened up. They are kept apart from the
/// read-only tools so that separation is visible rather than a matter of reading each description.
/// </remarks>
[McpServerToolType]
public sealed class HealthieActionTools(IPulsesScheduler pulsesScheduler)
{
    /// <summary>Runs a checker immediately and reports what it found.</summary>
    [McpServerTool(Name = "run_check")]
    [Description("Runs one component's health check immediately, instead of waiting for its next scheduled run, and returns the fresh result. Use this to confirm whether a problem is still happening.")]
    public async Task<CheckerSummary> RunCheckAsync(
        [Description("The name of the component, as reported by get_health_states.")] string name,
        CancellationToken cancellationToken)
    {
        var checker = await FindCheckerAsync(name, cancellationToken).ConfigureAwait(false);

        await checker.TriggerAsync(cancellationToken).ConfigureAwait(false);

        var state = await checker.GetStateAsync(cancellationToken).ConfigureAwait(false);

        return CheckerSummary.From(name, state);
    }

    /// <summary>Clears a checker's failure count and marks it healthy.</summary>
    [McpServerTool(Name = "reset_checker")]
    [Description("Clears a component's consecutive failure count and marks it healthy, without running the check. Use this after a problem has been fixed to clear the failure streak.")]
    public async Task<CheckerSummary> ResetCheckerAsync(
        [Description("The name of the component, as reported by get_health_states.")] string name,
        CancellationToken cancellationToken)
    {
        var checker = await FindCheckerAsync(name, cancellationToken).ConfigureAwait(false);

        await pulsesScheduler.ResetAsync(name, cancellationToken).ConfigureAwait(false);

        var state = await checker.GetStateAsync(cancellationToken).ConfigureAwait(false);

        return CheckerSummary.From(name, state);
    }

    private async Task<IPulseChecker> FindCheckerAsync(string name, CancellationToken cancellationToken)
    {
        var checkers = await pulsesScheduler.GetPulseCheckersAsync(cancellationToken).ConfigureAwait(false);

        return checkers.TryGetValue(name, out var checker)
            ? checker
            : throw new McpException($"No checker named '{name}'. Call get_health_states to list the available names.");
    }
}
