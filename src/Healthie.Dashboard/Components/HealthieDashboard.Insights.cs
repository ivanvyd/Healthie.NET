using Healthie.Abstractions.Insights;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Dashboard.Components;

/// <summary>
/// The parts of the board that appear only when a feature package is installed.
/// </summary>
/// <remarks>
/// <para>
/// Each of these is resolved from the container rather than injected, because <c>[Inject]</c> throws
/// when a service is absent and absent is the normal case: an application with no alerting installed
/// should get a board without an alerts panel, not an exception.
/// </para>
/// <para>
/// All of them read. Nothing here changes a checker, which is what lets the whole set show in
/// read-only mode -- the one exception is asking a language model to explain a failure, which is
/// still a read but costs money on someone else's account, so it is treated as an action.
/// </para>
/// </remarks>
public sealed partial class HealthieDashboard
{
    /// <summary>How far back the uptime panel looks.</summary>
    /// <remarks>
    /// A day, because that is the window an operator asks about first and the one an overnight
    /// incident falls inside. The board's own percentage covers the last few minutes.
    /// </remarks>
    private static readonly TimeSpan UptimeWindow = TimeSpan.FromHours(24);

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    private IUptimeInsights? _uptimeInsights;
    private IAlertInsights? _alertInsights;
    private ILeadershipInsights? _leadershipInsights;
    private IDiagnosisInsights? _diagnosisInsights;

    private UptimeInsight? _uptime;
    private string? _uptimeFor;

    private IReadOnlyList<AlertInsight> _recentAlerts = [];
    private bool _alertsOpen;

    private string? _diagnosis;
    private bool _diagnosing;

    /// <summary>Whether this replica is the one running the checks.</summary>
    /// <remarks>
    /// True when nothing is elected: a single replica runs everything, and saying "follower" there
    /// would be a warning about a situation that does not exist.
    /// </remarks>
    private bool IsLeader => _leadershipInsights?.IsLeader ?? true;

    /// <summary>Picks up whatever feature packages the application installed.</summary>
    private void ResolveInsights()
    {
        _uptimeInsights = Services.GetService<IUptimeInsights>();
        _alertInsights = Services.GetService<IAlertInsights>();
        _leadershipInsights = Services.GetService<ILeadershipInsights>();
        _diagnosisInsights = Services.GetService<IDiagnosisInsights>();
    }

    /// <summary>Reads the uptime for the selected checker.</summary>
    /// <remarks>
    /// Only on selection, not on the clock tick: it is a query over recorded segments, and the board
    /// redraws every second.
    /// </remarks>
    private async Task LoadUptimeAsync(string? checkerName)
    {
        if (_uptimeInsights is null || checkerName is null)
        {
            _uptime = null;
            _uptimeFor = null;
            return;
        }

        if (_uptimeFor == checkerName)
        {
            return;
        }

        _uptimeFor = checkerName;
        _uptime = await _uptimeInsights.GetUptimeAsync(checkerName, UptimeWindow);
    }

    /// <summary>Reads the recent alerts, newest first.</summary>
    private async Task LoadAlertsAsync()
    {
        if (_alertInsights is null)
        {
            return;
        }

        _recentAlerts = await _alertInsights.GetRecentAlertsAsync(AlertsShown);
    }

    /// <summary>How many alerts the panel lists.</summary>
    private const int AlertsShown = 20;

    private async Task ToggleAlertsAsync()
    {
        _alertsOpen = !_alertsOpen;

        if (_alertsOpen)
        {
            await LoadAlertsAsync();
        }
    }

    /// <summary>Asks the model why a checker has been failing.</summary>
    /// <remarks>
    /// Guarded against a second click while the first is in flight: the call takes seconds and costs
    /// money, and a button that looks unresponsive invites exactly that.
    /// </remarks>
    private async Task ExplainAsync(string checkerName)
    {
        if (_diagnosisInsights is null || _diagnosing)
        {
            return;
        }

        _diagnosing = true;
        _diagnosis = null;

        try
        {
            _diagnosis = await _diagnosisInsights.ExplainAsync(checkerName);
        }
        catch (Exception ex)
        {
            // Shown rather than swallowed: a model that is misconfigured or out of quota should say
            // so on the board, not leave a button that appears to do nothing.
            _diagnosis = $"Could not explain this: {ex.Message}";
        }
        finally
        {
            _diagnosing = false;
        }
    }

    /// <summary>Formats an uptime percentage the way the rest of the board formats one.</summary>
    private static string Percent(double value) => $"{value:0.##}%";

    /// <summary>A short, human length: "4m", "2h 10m".</summary>
    private static string Duration(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds:0}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes:0}m",
        { TotalHours: < 24 } => $"{(int)span.TotalHours}h {span.Minutes}m",
        _ => $"{(int)span.TotalDays}d {span.Hours}h",
    };
}
