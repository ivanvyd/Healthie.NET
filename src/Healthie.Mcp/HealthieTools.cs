using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Healthie.Mcp;

/// <summary>
/// The read-only tools an MCP client can call to inspect pulse checker health.
/// </summary>
/// <remarks>
/// Descriptions are what a model reads to decide which tool to call, so they describe when a tool is
/// useful rather than only what it returns. Tools that change state live in
/// <see cref="HealthieActionTools"/> and are only exposed when they are turned on.
/// </remarks>
[McpServerToolType]
public sealed class HealthieTools(IPulsesScheduler pulsesScheduler)
{
    /// <summary>Returns the current health of every pulse checker.</summary>
    [McpServerTool(Name = "get_health_states")]
    [Description("Returns the current health of every monitored component, including the last result and when it last ran. Start here to see the overall health of the system.")]
    public async Task<IReadOnlyList<CheckerSummary>> GetHealthStatesAsync(CancellationToken cancellationToken)
    {
        var states = await pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false);

        return [.. states.Select(entry => CheckerSummary.From(entry.Key, entry.Value))];
    }

    /// <summary>Returns the checkers that are not currently healthy.</summary>
    [McpServerTool(Name = "get_unhealthy_checkers")]
    [Description("Returns only the components that are currently unhealthy or suspicious. Use this to find what is wrong without reading through healthy components.")]
    public async Task<IReadOnlyList<CheckerSummary>> GetUnhealthyCheckersAsync(CancellationToken cancellationToken)
    {
        var states = await pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. states
                .Where(entry => entry.Value.LastResult is not null
                    && entry.Value.LastResult.Health != PulseCheckerHealth.Healthy)
                .Select(entry => CheckerSummary.From(entry.Key, entry.Value)),
        ];
    }

    /// <summary>Returns one checker's current state and configuration.</summary>
    [McpServerTool(Name = "get_checker")]
    [Description("Returns one component's current health and its configuration, including how often it runs and how many consecutive failures it tolerates before being called unhealthy.")]
    public async Task<CheckerDetail> GetCheckerAsync(
        [Description("The name of the component, as reported by get_health_states.")] string name,
        CancellationToken cancellationToken)
    {
        var states = await pulsesScheduler.GetPulsesStatesAsync(cancellationToken).ConfigureAwait(false);

        if (!states.TryGetValue(name, out var state))
        {
            throw new McpException($"No checker named '{name}'. Call get_health_states to list the available names.");
        }

        return CheckerDetail.From(name, state);
    }

    /// <summary>Returns a page of one checker's recorded run history, newest first.</summary>
    [McpServerTool(Name = "get_check_history")]
    [Description("Returns the recent run history of one component, newest first. Use this to see how long a component has been failing and what it reported over time.")]
    public async Task<HistoryPage> GetCheckHistoryAsync(
        [Description("The name of the component, as reported by get_health_states.")] string name,
        [Description("How many entries to return. Defaults to 20 and is capped by the server.")] int limit = 20,
        [Description("How many of the most recent entries to skip, for paging through older history.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var history = await pulsesScheduler.GetHistoryAsync(name, cancellationToken).ConfigureAwait(false);

        // History is recorded oldest-first; a reader almost always wants the newest first.
        var newestFirst = history.AsEnumerable().Reverse().ToList();
        var page = newestFirst.Skip(Math.Max(offset, 0)).Take(Math.Clamp(limit, 1, 200)).ToList();

        return new HistoryPage(
            name,
            [.. page.Select(entry => new HistoryEntry(entry.Health.ToString(), entry.Message, entry.ExecutedAt))],
            newestFirst.Count,
            offset + page.Count < newestFirst.Count);
    }
}

/// <summary>A checker's name and current health.</summary>
/// <param name="Name">The name identifying the checker.</param>
/// <param name="Health">The health reported by the last run, or <c>null</c> if it has not run yet.</param>
/// <param name="Message">What the last run reported.</param>
/// <param name="LastRanAt">When the checker last ran.</param>
/// <param name="IsActive">Whether the checker is currently scheduled to run.</param>
public record CheckerSummary(string Name, string? Health, string? Message, DateTime? LastRanAt, bool IsActive)
{
    internal static CheckerSummary From(string name, PulseCheckerState state) => new(
        name,
        state.LastResult?.Health.ToString(),
        state.LastResult?.Message,
        state.LastExecutionDateTime,
        state.IsActive);
}

/// <summary>A checker's current health together with its configuration.</summary>
/// <param name="Name">The name identifying the checker.</param>
/// <param name="Health">The health reported by the last run, or <c>null</c> if it has not run yet.</param>
/// <param name="Message">What the last run reported.</param>
/// <param name="LastRanAt">When the checker last ran.</param>
/// <param name="IsActive">Whether the checker is currently scheduled to run.</param>
/// <param name="Interval">How often the checker runs.</param>
/// <param name="ConsecutiveFailures">How many times in a row the checker has failed.</param>
/// <param name="UnhealthyThreshold">How many consecutive failures are tolerated before the checker is called unhealthy.</param>
/// <param name="HistoryEntryCount">How many history entries are currently recorded.</param>
public record CheckerDetail(
    string Name,
    string? Health,
    string? Message,
    DateTime? LastRanAt,
    bool IsActive,
    string Interval,
    int ConsecutiveFailures,
    uint UnhealthyThreshold,
    int HistoryEntryCount)
{
    internal static CheckerDetail From(string name, PulseCheckerState state) => new(
        name,
        state.LastResult?.Health.ToString(),
        state.LastResult?.Message,
        state.LastExecutionDateTime,
        state.IsActive,
        state.Interval.ToString(),
        state.ConsecutiveFailureCount,
        state.UnhealthyThreshold,
        state.History.Count);
}

/// <summary>A page of history entries, newest first.</summary>
/// <param name="Name">The checker the history belongs to.</param>
/// <param name="Entries">The entries in this page.</param>
/// <param name="TotalCount">How many entries are recorded in total.</param>
/// <param name="HasMore">Whether older entries remain beyond this page.</param>
public record HistoryPage(string Name, IReadOnlyList<HistoryEntry> Entries, int TotalCount, bool HasMore);

/// <summary>A single recorded run.</summary>
/// <param name="Health">The health reported by the run.</param>
/// <param name="Message">What the run reported.</param>
/// <param name="ExecutedAt">When the run happened.</param>
public record HistoryEntry(string Health, string? Message, DateTime ExecutedAt);
