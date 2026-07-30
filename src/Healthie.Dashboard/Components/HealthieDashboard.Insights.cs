using Healthie.Abstractions.Enums;
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

    /// <summary>How many alerts a page of the history holds.</summary>
    private const int AlertsPerPage = 25;

    private IUptimeInsights? _uptimeInsights;
    private IAlertInsights? _alertInsights;
    private ILeadershipInsights? _leadershipInsights;
    private IDiagnosisInsights? _diagnosisInsights;
    private IMetricsInsights? _metricsInsights;
    private IAlertConfiguration? _alertConfiguration;

    private UptimeInsight? _uptime;

    private AlertPage? _alerts;
    private int _alertPage;
    private bool _undeliveredOnly;

    private MetricsSnapshot? _metrics;

    private AlertSettings? _settingsDraft;
    private string? _settingsError;
    private IReadOnlyList<AlertSinkStatus>? _testResult;
    private bool _testing;

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
        _metricsInsights = Services.GetService<IMetricsInsights>();
        _alertConfiguration = Services.GetService<IAlertConfiguration>();
        _settingsDraft = _alertConfiguration?.Current;
    }

    /// <summary>Reads the uptime for the selected checker.</summary>
    /// <remarks>
    /// On selection, not on the clock tick: it is a query over recorded segments and the board
    /// redraws every second. Re-selecting a checker re-reads rather than short-circuiting, so
    /// clicking the row you are on is how you refresh a window that has been open a while.
    /// </remarks>
    private async Task LoadUptimeAsync(string? checkerName)
    {
        if (_uptimeInsights is null || checkerName is null)
        {
            _uptime = null;

            return;
        }

        _uptime = await _uptimeInsights.GetUptimeAsync(checkerName, UptimeWindow);
    }

    /// <summary>Reads one page of the alert history, newest first.</summary>
    private async Task LoadAlertsAsync()
    {
        if (_alertInsights is null)
        {
            return;
        }

        _alerts = await _alertInsights.GetAlertsAsync(_alertPage * AlertsPerPage, AlertsPerPage);
    }

    /// <summary>The alerts this page shows, after the undelivered filter.</summary>
    /// <remarks>
    /// Filtered here rather than in the query: the store pages over everything it holds, and a filter
    /// pushed into it would make the page counts disagree with the pager. This narrows what is shown
    /// on the page you are on, which is what the toggle says it does.
    /// </remarks>
    private IReadOnlyList<AlertInsight> VisibleAlerts =>
        _alerts is null
            ? []
            : _undeliveredOnly
                ? [.. _alerts.Alerts.Where(alert => !alert.Delivered)]
                : _alerts.Alerts;

    private int AlertPageCount =>
        _alerts is null || _alerts.Total == 0 ? 1 : (_alerts.Total + AlertsPerPage - 1) / AlertsPerPage;

    private async Task GoToAlertPageAsync(int page)
    {
        _alertPage = Math.Clamp(page, 0, AlertPageCount - 1);

        await LoadAlertsAsync();
    }

    private async Task ToggleUndeliveredOnlyAsync()
    {
        _undeliveredOnly = !_undeliveredOnly;

        await LoadAlertsAsync();
    }

    /// <summary>Reads what the library's own instruments have counted.</summary>
    private void LoadMetrics() => _metrics = _metricsInsights?.Snapshot();

    /// <summary>
    /// Applies the alerting settings in the form, from the next alert onwards.
    /// </summary>
    private void ApplyAlertSettings()
    {
        if (_alertConfiguration is null || _settingsDraft is null)
        {
            return;
        }

        _settingsError = null;

        try
        {
            _alertConfiguration.Apply(_settingsDraft);
            AddEvent("CONF", Status.Paused, "Alerting settings changed");
        }
        catch (ArgumentException ex)
        {
            _settingsError = ex.Message;
        }
    }

    /// <summary>
    /// Sends one alert through the real sinks, because the only way to find out that a webhook URL is
    /// wrong is to use it.
    /// </summary>
    private async Task SendTestAlertAsync()
    {
        if (_alertConfiguration is null || _testing)
        {
            return;
        }

        _testing = true;
        _testResult = null;

        try
        {
            _testResult = await _alertConfiguration.SendTestAlertAsync();
            AddEvent("TEST", Status.Ok, "Test alert sent to every sink");
            await LoadAlertsAsync();
        }
        catch (Exception ex)
        {
            _settingsError = $"Could not send the test alert: {ex.Message}";
        }
        finally
        {
            _testing = false;
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

    /// <summary>A check duration, in the unit a person reads durations of checks in.</summary>
    private static string Milliseconds(TimeSpan span) =>
        span.TotalMilliseconds < 1000 ? $"{span.TotalMilliseconds:0} ms" : $"{span.TotalSeconds:0.##} s";

    /// <summary>The status class for a health, so an alert row is coloured like everything else.</summary>
    private static string HealthClass(PulseCheckerHealth? health) => health switch
    {
        PulseCheckerHealth.Healthy => "hpm-ok",
        PulseCheckerHealth.Suspicious => "hpm-warn",
        PulseCheckerHealth.Unhealthy => "hpm-crit",
        _ => "hpm-paused",
    };

    /// <summary>A short, human length: "4m", "2h 10m".</summary>
    private static string Duration(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds:0}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes:0}m",
        { TotalHours: < 24 } => $"{(int)span.TotalHours}h {span.Minutes}m",
        _ => $"{(int)span.TotalDays}d {span.Hours}h",
    };
}
