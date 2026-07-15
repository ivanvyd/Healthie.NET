using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Api;

/// <summary>
/// Extension methods for exposing pulse checker state as probe endpoints, in the shape an
/// orchestrator such as Kubernetes expects.
/// </summary>
public static class ProbeStartupExtensions
{
    /// <summary>The path served by <see cref="MapHealthieLiveness"/>.</summary>
    public const string LivenessPath = "/healthie/live";

    /// <summary>The path served by <see cref="MapHealthieReadiness"/>.</summary>
    public const string ReadinessPath = "/healthie/ready";

    /// <summary>
    /// Maps a liveness probe that reports whether the process is running.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for further endpoint configuration.</returns>
    /// <remarks>
    /// The probe answers "is this process alive", not "is everything it monitors healthy", and so it
    /// deliberately ignores checker state. A liveness probe that fails on a failing dependency would
    /// have the orchestrator restart a process that is working correctly and reporting a problem
    /// elsewhere. Use <see cref="MapHealthieReadiness"/> to gate traffic on checker state.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <c>null</c>.</exception>
    public static IEndpointConventionBuilder MapHealthieLiveness(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet(LivenessPath, () => Results.Text("Healthy"));
    }

    /// <summary>
    /// Maps a readiness probe that reports the worst health across all active pulse checkers.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="failOnSuspicious">
    /// Whether a suspicious checker makes the probe fail. Defaults to <c>false</c>, so a failure that
    /// has not yet crossed its threshold does not take the instance out of rotation.
    /// </param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for further endpoint configuration.</returns>
    /// <remarks>
    /// Responds 200 when ready and 503 when not, which is what an orchestrator reads. Stopped
    /// checkers are ignored: a checker that was deliberately paused is not a reason to refuse
    /// traffic. Checkers that have not run yet are treated as healthy, so an instance is not held out
    /// of rotation purely because its first check has not happened.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <c>null</c>.</exception>
    public static IEndpointConventionBuilder MapHealthieReadiness(
        this IEndpointRouteBuilder endpoints,
        bool failOnSuspicious = false)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet(ReadinessPath, async (
            IPulsesScheduler pulsesScheduler,
            CancellationToken cancellationToken) =>
        {
            var states = await pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false);
            var worst = WorstHealthOf(states.Values);

            return IsReady(worst, failOnSuspicious)
                ? Results.Text(worst.ToString())
                : Results.Text(worst.ToString(), statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }

    /// <remarks>
    /// Compares by the enum's own order, which runs from healthy to unhealthy by increasing
    /// severity.
    /// </remarks>
    private static PulseCheckerHealth WorstHealthOf(IEnumerable<PulseCheckerState> states)
    {
        var worst = PulseCheckerHealth.Healthy;

        foreach (var state in states)
        {
            if (!state.IsActive || state.LastResult is null)
            {
                continue;
            }

            if (state.LastResult.Health > worst)
            {
                worst = state.LastResult.Health;
            }
        }

        return worst;
    }

    private static bool IsReady(PulseCheckerHealth worst, bool failOnSuspicious) => worst switch
    {
        PulseCheckerHealth.Healthy => true,
        PulseCheckerHealth.Suspicious => !failOnSuspicious,
        _ => false,
    };
}
