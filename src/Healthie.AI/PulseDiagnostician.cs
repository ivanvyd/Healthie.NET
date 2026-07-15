using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Microsoft.Extensions.AI;
using System.Text;

namespace Healthie.AI;

/// <summary>
/// Explains, in plain language, what a pulse checker's recent history shows.
/// </summary>
/// <remarks>
/// <para>
/// Works against any <see cref="IChatClient"/>, so the host chooses the provider -- Anthropic,
/// OpenAI, Azure OpenAI, or a local model through Ollama -- and this package depends on none of
/// them.
/// </para>
/// <para>
/// Only a checker's name, health, and the messages its own checks reported are sent to the model.
/// Those messages are written by the checkers in the host application, so anything a check puts in
/// its message leaves the process when a diagnosis is requested. This is why check results should
/// not carry credentials or personal data.
/// </para>
/// </remarks>
public sealed class PulseDiagnostician(IChatClient chatClient, IPulsesScheduler pulsesScheduler) : IPulseDiagnostician
{
    private const string SystemPrompt =
        "You are helping an engineer read a health monitor. You are given one component's recent " +
        "check results, oldest first. Say what the pattern shows: whether it is failing " +
        "consistently or intermittently, when it started, and what the error messages point to as " +
        "the likely cause. Be specific about what the evidence supports and say plainly when the " +
        "evidence is too thin to tell. Do not invent details that are not in the data. Keep it to " +
        "a short paragraph.";

    private readonly IChatClient _chatClient = chatClient
        ?? throw new ArgumentNullException(nameof(chatClient));

    private readonly IPulsesScheduler _pulsesScheduler = pulsesScheduler
        ?? throw new ArgumentNullException(nameof(pulsesScheduler));

    private readonly FailureRateAnomalyDetector _anomalyDetector = new();

    /// <inheritdoc />
    public async Task<PulseDiagnosis> DiagnoseAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var states = await _pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false);

        if (!states.TryGetValue(name, out var state))
        {
            throw new ArgumentException($"Pulse checker with name '{name}' not found.", nameof(name));
        }

        var history = await _pulsesScheduler.GetHistoryAsync(name, cancellationToken).ConfigureAwait(false);

        if (history.Count == 0)
        {
            return new PulseDiagnosis(
                name,
                "This checker has not recorded any runs yet, so there is nothing to diagnose.",
                AnomalyReport.NotEnoughHistory);
        }

        var anomaly = _anomalyDetector.Detect(history);
        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, DescribeHistory(name, state, history)),
            ],
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new PulseDiagnosis(name, response.Text, anomaly);
    }

    /// <summary>
    /// Renders a checker's state and history as the plain text handed to the model.
    /// </summary>
    private static string DescribeHistory(
        string name,
        PulseCheckerState state,
        IReadOnlyList<PulseCheckerHistoryEntry> history)
    {
        var description = new StringBuilder();

        description.AppendLine($"Component: {name}");
        description.AppendLine($"Currently: {state.LastResult?.Health.ToString() ?? "not yet run"}");
        description.AppendLine($"Consecutive failures: {state.ConsecutiveFailureCount}");
        description.AppendLine($"Failures tolerated before being called unhealthy: {state.UnhealthyThreshold}");
        description.AppendLine($"Runs every: {state.Interval}");
        description.AppendLine();
        description.AppendLine("Recent checks, oldest first:");

        foreach (var entry in history)
        {
            description.AppendLine(
                $"  {entry.ExecutedAt:u}  {entry.Health,-10}  {entry.Message ?? "(no message)"}");
        }

        return description.ToString();
    }
}
