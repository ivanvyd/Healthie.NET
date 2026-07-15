using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Models;
using Healthie.Dashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.ComponentModel;
using System.Reflection;

namespace Healthie.Dashboard.Components;

/// <summary>
/// The Healthie.NET dashboard: every pulse checker, its recent runs, and the controls for one of
/// them.
/// </summary>
/// <remarks>
/// State arrives by event rather than polling -- the component subscribes to each checker and
/// updates the one entry that changed. The only timer here drives the clock and the "3s ago"
/// labels, which nothing pushes.
/// </remarks>
public sealed partial class HealthieDashboard : IAsyncDisposable
{
    private const string WordMark = "HEALTHIE·PULSE";

    private const int MaxEvents = 14;

    /// <summary>How often the clock and the relative timestamps are refreshed.</summary>
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);

    private static readonly PulseInterval[] Intervals = Enum.GetValues<PulseInterval>();

    /// <summary>Unique per instance so two dashboards on one page cannot share an SVG pattern id.</summary>
    private readonly string _traceId = $"hpm-trace-{Guid.NewGuid():N}";

    private readonly List<EventEntry> _events = [];
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();

    private Dictionary<string, PulseCheckerState> _states = [];
    private Dictionary<string, string> _displayNames = [];
    private List<KeyValuePair<string, PulseCheckerState>> _filtered = [];

    private string? _selected;
    private string? _searchFilter;
    private bool _isDarkMode = true;
    private bool _isLoading = true;
    private bool _initialized;
    private DateTime _now = DateTime.Now;
    private Status _overall = Status.Ok;
    private Task? _clockLoop;

    private PulseCheckerState? _selectedState =>
        _selected is not null && _states.TryGetValue(_selected, out var state) ? state : null;

    private string OverallLabel
    {
        get
        {
            var unhealthy = _states.Values.Count(s => HealthOf(s) == PulseCheckerHealth.Unhealthy);
            if (unhealthy > 0)
            {
                return $"{unhealthy} FAILING";
            }

            var suspicious = _states.Values.Count(s => HealthOf(s) == PulseCheckerHealth.Suspicious);

            return suspicious > 0 ? $"{suspicious} SUSPICIOUS" : "ALL SYSTEMS OK";
        }
    }

    private string TraceColor => _overall switch
    {
        Status.Critical => "var(--hpm-crit)",
        Status.Warning => "var(--hpm-warn)",
        _ => "var(--hpm-ok)",
    };

    /// <summary>How many checks a minute the active checkers add up to.</summary>
    private string ChecksPerMinute => _states.Values
        .Where(state => state.IsActive)
        .Sum(state => 60d / state.Interval.ToTimeSpan().TotalSeconds)
        .ToString("0");

    /// <summary>How many runs the sparkline can show, which is however many are kept.</summary>
    private int HistoryWindow => _states.Count == 0 ? 0 : _states.Values.Max(s => s.History.Count);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _isDarkMode = ThemeState.IsDarkMode;

        await DashboardService.SubscribeToStateChangesAsync(OnStateChangedAsync);
        await LoadAsync();

        _clockLoop = RunClockAsync();
    }

    private async Task LoadAsync()
    {
        if (!await _loadLock.WaitAsync(TimeSpan.Zero).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _states = await DashboardService.GetAllStatesAsync();
            _displayNames = await DashboardService.GetDisplayNamesAsync();

            _selected ??= _states.Keys.OrderBy(name => name).FirstOrDefault();
            _isLoading = false;
            Refresh();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Refreshes the clock and the relative timestamps, which no event announces.
    /// </summary>
    private async Task RunClockAsync()
    {
        using var timer = new PeriodicTimer(ClockInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(_disposing.Token))
            {
                _now = DateTime.Now;
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // The circuit is going away.
        }
    }

    /// <summary>
    /// Applies one checker's new state, recording an event when its health changed.
    /// </summary>
    private Task OnStateChangedAsync(string name, PulseCheckerState state)
    {
        var previous = _states.TryGetValue(name, out var existing) ? existing : null;

        _states[name] = state;

        if (previous is not null)
        {
            RecordTransition(name, previous, state);
        }

        Refresh();

        return Task.CompletedTask;
    }

    private void RecordTransition(string name, PulseCheckerState previous, PulseCheckerState current)
    {
        if (previous.IsActive != current.IsActive)
        {
            AddEvent(
                current.IsActive ? "START" : "PAUSE",
                current.IsActive ? Status.Ok : Status.Paused,
                $"{DisplayNameOf(name)} {(current.IsActive ? "started" : "paused by operator")}");

            return;
        }

        var was = HealthOf(previous);
        var now = HealthOf(current);

        if (was == now)
        {
            return;
        }

        var (tag, status) = now switch
        {
            PulseCheckerHealth.Healthy => ("OK", Status.Ok),
            PulseCheckerHealth.Suspicious => ("WARN", Status.Warning),
            _ => ("FAIL", Status.Critical),
        };

        var detail = now == PulseCheckerHealth.Healthy
            ? "recovered"
            : current.LastResult?.Message ?? now.ToString();

        AddEvent(tag, status, $"{DisplayNameOf(name)}: {detail}");
    }

    private void AddEvent(string tag, Status status, string text)
    {
        _events.Insert(0, new EventEntry(DateTime.Now, tag, status, text));

        if (_events.Count > MaxEvents)
        {
            _events.RemoveRange(MaxEvents, _events.Count - MaxEvents);
        }
    }

    private void Refresh()
    {
        var filtered = _states.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            filtered = filtered.Where(entry =>
                DisplayNameOf(entry.Key).Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
        }

        _filtered = [.. filtered.OrderBy(entry => DisplayNameOf(entry.Key), StringComparer.OrdinalIgnoreCase)];

        var healths = _states.Values.Select(HealthOf).ToList();
        _overall =
            healths.Contains(PulseCheckerHealth.Unhealthy) ? Status.Critical :
            healths.Contains(PulseCheckerHealth.Suspicious) ? Status.Warning :
            Status.Ok;

        StateHasChanged();
    }

    private void OnSearchInput(ChangeEventArgs args)
    {
        _searchFilter = args.Value?.ToString();
        Refresh();
    }

    private void OnRowKeyDown(KeyboardEventArgs args, string name)
    {
        if (args.Key is "Enter" or " ")
        {
            _selected = name;
        }
    }

    private async Task ToggleThemeAsync()
    {
        _isDarkMode = !_isDarkMode;
        ThemeState.IsDarkMode = _isDarkMode;

        await Task.CompletedTask;
    }

    private async Task TriggerAllAsync()
    {
        await DashboardService.TriggerAllAsync();
        AddEvent("RUN", Status.Ok, "All checkers triggered manually");
    }

    private async Task StartAllAsync() => await DashboardService.StartAllAsync();

    private async Task StopAllAsync() => await DashboardService.StopAllAsync();

    private async Task TriggerCheckerAsync(string name)
    {
        await DashboardService.TriggerAsync(name);
        AddEvent("RUN", Status.Ok, $"{DisplayNameOf(name)} triggered manually");
    }

    private async Task ToggleCheckerAsync(string name, bool isActive)
    {
        if (isActive)
        {
            await DashboardService.StopAsync(name);
        }
        else
        {
            await DashboardService.StartAsync(name);
        }
    }

    private async Task ResetCheckerAsync(string name)
    {
        await DashboardService.ResetAsync(name);
        AddEvent("RESET", Status.Warning, $"{DisplayNameOf(name)} state reset to healthy");
    }

    private async Task OnIntervalChanged(ChangeEventArgs args)
    {
        if (_selected is null || !Enum.TryParse<PulseInterval>(args.Value?.ToString(), out var interval))
        {
            return;
        }

        await DashboardService.SetIntervalAsync(_selected, interval);
        AddEvent("CONF", Status.Paused, $"{DisplayNameOf(_selected)} interval set to {interval}");
    }

    private async Task OnThresholdChanged(ChangeEventArgs args)
    {
        if (_selected is null ||
            !uint.TryParse(args.Value?.ToString(), out var threshold))
        {
            return;
        }

        await DashboardService.SetThresholdAsync(_selected, threshold);
        AddEvent("CONF", Status.Paused, $"{DisplayNameOf(_selected)} threshold set to {threshold}");
    }

    // A checker's state already carries its history, so it is read from there rather than
    // fetched separately: a second read would cost another provider round-trip per checker.
    private List<PulseCheckerHistoryEntry> HistoryOf(string name) =>
        _states.TryGetValue(name, out var state) ? state.History : [];

    private string DisplayNameOf(string name) =>
        _displayNames.TryGetValue(name, out var displayName) && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : ShortTypeName(name);

    private static string ShortTypeName(string name)
    {
        var lastDot = name.LastIndexOf('.');

        return lastDot >= 0 && lastDot < name.Length - 1 ? name[(lastDot + 1)..] : name;
    }

    /// <summary>A checker that has not run yet is treated as healthy rather than as a failure.</summary>
    private static PulseCheckerHealth HealthOf(PulseCheckerState state) =>
        state.LastResult?.Health ?? PulseCheckerHealth.Healthy;

    private static Status StatusOf(PulseCheckerState state) =>
        !state.IsActive ? Status.Paused : StatusOf(HealthOf(state));

    private static Status StatusOf(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Healthy => Status.Ok,
        PulseCheckerHealth.Suspicious => Status.Warning,
        _ => Status.Critical,
    };

    private static string StatusClass(Status status) => status switch
    {
        Status.Ok => "hpm-ok",
        Status.Warning => "hpm-warn",
        Status.Critical => "hpm-crit",
        _ => "hpm-paused",
    };

    private static string BlipClass(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Healthy => "hpm-blip--ok",
        PulseCheckerHealth.Suspicious => "hpm-blip--warn",
        _ => "hpm-blip--crit",
    };

    private static string StatusWord(PulseCheckerState state) =>
        !state.IsActive ? "PAUSED"
        : state.LastResult is null ? "PENDING"
        : state.LastResult.Health.ToString().ToUpperInvariant();

    private static string RatePerMinute(PulseCheckerState state)
    {
        var perMinute = 60d / state.Interval.ToTimeSpan().TotalSeconds;

        return perMinute >= 1 ? Math.Round(perMinute).ToString("0") : perMinute.ToString("0.0");
    }

    private static string FailuresLabel(PulseCheckerState state) =>
        state.UnhealthyThreshold > 0
            ? $"{state.ConsecutiveFailureCount}/{state.UnhealthyThreshold}"
            : state.ConsecutiveFailureCount.ToString();

    /// <summary>The share of recorded runs that passed.</summary>
    private string Uptime(string name)
    {
        var history = HistoryOf(name);

        if (history.Count == 0)
        {
            return "--";
        }

        var passed = history.Count(entry => entry.Health == PulseCheckerHealth.Healthy);

        return $"{passed * 100 / history.Count}%";
    }

    private string UptimeColor(string name)
    {
        var history = HistoryOf(name);

        return history.Count > 0 && history.All(entry => entry.Health == PulseCheckerHealth.Healthy)
            ? "var(--hpm-ok)"
            : "var(--hpm-text)";
    }

    private string Relative(DateTime? executedAt)
    {
        if (executedAt is null)
        {
            return "never";
        }

        var elapsed = _now.ToUniversalTime() - executedAt.Value.ToUniversalTime();

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalSeconds < 5 ? "just now"
            : elapsed.TotalMinutes < 1 ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m ago"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h ago"
            : $"{(int)elapsed.TotalDays}d ago";
    }

    /// <summary>Reads an interval's own description rather than restating the list here.</summary>
    private static string Describe(PulseInterval interval) =>
        typeof(PulseInterval)
            .GetField(interval.ToString())?
            .GetCustomAttribute<DescriptionAttribute>()?
            .Description
        ?? interval.ToString();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _disposing.CancelAsync();

        if (_clockLoop is not null)
        {
            await _clockLoop;
        }

        _disposing.Dispose();
        _loadLock.Dispose();
    }

    /// <summary>What a colour in this dashboard means.</summary>
    private enum Status
    {
        Ok,
        Warning,
        Critical,
        Paused,
    }

    /// <param name="At">When the event happened, in local time.</param>
    /// <param name="Tag">The short label shown in the log.</param>
    /// <param name="Status">The colour the entry is shown in.</param>
    /// <param name="Text">What happened.</param>
    private sealed record EventEntry(DateTime At, string Tag, Status Status, string Text);
}
