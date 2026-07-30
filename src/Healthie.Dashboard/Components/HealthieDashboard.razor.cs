using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Extensions;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
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

    /// <summary>
    /// How many events are remembered. The side panel only ever shows the newest handful, but the
    /// expanded log is worth opening precisely when something has gone wrong and the interesting
    /// part has already scrolled past, so it keeps rather more than fits.
    /// </summary>
    /// <remarks>
    /// Held per circuit and never persisted: this is a running commentary on what the dashboard has
    /// watched happen, not a record. It starts empty on a refresh.
    /// </remarks>
    private const int MaxEvents = 200;

    /// <summary>How often the clock and the relative timestamps are refreshed.</summary>
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);

    private static readonly PulseInterval[] Intervals = Enum.GetValues<PulseInterval>();

    /// <summary>Unique per instance so two dashboards on one page cannot share an SVG pattern id.</summary>
    private readonly string _traceId = $"hpm-trace-{Guid.NewGuid():N}";

    private readonly List<EventEntry> _events = [];
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly CancellationTokenSource _disposing = new();

    /// <summary>The key under which the prerender persists its handoff token.</summary>
    private const string HandoffTokenKey = "healthie.dashboard.handoff";

    private PersistingComponentStateSubscription _persistSubscription;

    private Dictionary<string, PulseCheckerState> _states = [];
    private Dictionary<string, string> _displayNames = [];
    private List<KeyValuePair<string, PulseCheckerState>> _filtered = [];

    /// <summary>The heading gathering checkers that were given no group.</summary>
    private const string UngroupedName = "UNGROUPED";

    private string? _selected;
    private string? _searchFilter;
    private string? _tagFilter;
    private bool _isDarkMode = true;
    private bool _isLoading = true;
    private bool _initialized;
    private bool _showAbout;
    private bool _showLog;
    private bool _asCards;

    /// <summary>
    /// Sectioned by group on open, flat on request.
    /// </summary>
    /// <remarks>
    /// A group is a partition -- every checker under exactly one heading, tallies that add up -- so
    /// the sectioned view is the one that answers "what is wrong, and where" at a glance. A flat
    /// list of forty checkers makes the reader do that grouping in their head. Checkers given no
    /// group collect under one heading rather than disappearing, so nothing is hidden by the
    /// default.
    /// </remarks>
    private bool _sectionByGroup = true;

    private bool _isNamingGroup;
    private string? _tagDraft;
    private string? _groupDraft;

    /// <summary>
    /// Whether the side menu is expanded. Open on a desktop-width first load.
    /// </summary>
    /// <remarks>
    /// The rail collapses to its icons rather than disappearing, so the toggle is always reachable
    /// and the layout does not reflow to a different set of controls.
    /// </remarks>
    private bool _navOpen = true;

    /// <summary>
    /// The group the menu has narrowed the list to, or <c>null</c> for everything.
    /// </summary>
    /// <remarks>
    /// A separate filter from <c>_tagFilter</c> and the search box, and combined with them rather
    /// than replacing them: picking a group in the menu should not silently clear a search someone
    /// has typed.
    /// </remarks>
    private string? _navSection;

    /// <summary>What is in the cron box, which is not the stored schedule until it is applied.</summary>
    private string? _cronDraft;

    /// <summary>Why the last schedule was refused, shown beside the box that was typed into.</summary>
    private string? _scheduleError;

    /// <summary>Marks the "new group" choice in the group picker, which no real group can collide with.</summary>
    private const string NewGroupOption = "\u0000new";

    /// <summary>Groups the caller has collapsed. Expanded is the default, so this starts empty.</summary>
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Everything is shown in UTC, deliberately.
    /// </summary>
    /// <remarks>
    /// This renders on the server, so a "local" time here would be the server's local time shown to
    /// a viewer who may be nowhere near it -- a clock that is wrong for everyone but the host. The
    /// library stores UTC, so UTC is what is displayed, and every viewer reads the same clock.
    /// </remarks>
    private DateTime _now = DateTime.UtcNow;

    private Status _overall = Status.Ok;
    private Task? _clockLoop;

    /// <summary>Every tag in use, for the filter dropdown.</summary>
    private IEnumerable<string> AllTags => _states.Values
        .SelectMany(state => state.Tags)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every group in use, offered when moving a checker so that groups converge on the ones that
    /// exist rather than sprouting a near-duplicate each time someone types.
    /// </summary>
    private IEnumerable<string> AllGroups => _states.Values
        .Select(state => state.Group)
        .Where(group => !string.IsNullOrWhiteSpace(group))
        .Select(group => group!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shipped version, read from the assembly rather than written down here, so the about
    /// panel cannot claim a version this build is not.
    /// </summary>
    private static string PackageVersion =>
        typeof(HealthieDashboard).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(HealthieDashboard).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private const string ProjectUrl = "https://github.com/ivanvyd/Healthie.NET";
    private const string AuthorName = "Ivan Vydrin";
    private const string AuthorUrl = "https://github.com/ivanvyd";
    private const string LicenseUrl = "https://github.com/ivanvyd/Healthie.NET/blob/main/LICENSE";

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

    /// <summary>
    /// How many checks a minute the active checkers add up to.
    /// </summary>
    /// <remarks>
    /// Cron checkers are left out rather than guessed at: "every weekday at 06:00" has no rate a
    /// minute, and folding in the interval they are not running on is what this used to do.
    /// <see cref="CronCheckerCount"/> reports them separately so they are not silently missing.
    /// </remarks>
    private string ChecksPerMinute => _states.Values
        .Where(state => state.IsActive && !state.EffectiveSchedule.IsCron)
        .Sum(state => 60d / state.EffectiveSchedule.Period!.Value.TotalSeconds)
        .ToString("0");

    /// <summary>How many active checkers run on a cron expression instead of a rate.</summary>
    private int CronCheckerCount => _states.Values.Count(state => state.IsActive && state.EffectiveSchedule.IsCron);

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

        // Whatever feature packages the application installed. Absent is the normal case.
        ResolveInsights();

        // Hand the states the prerender read across to the interactive render. Without this the
        // circuit starts from an empty board and reads every checker again; the board it renders in
        // the meantime -- momentarily empty, then repopulating -- replaces the one the prerender
        // already painted, a visible flicker on every load. The handoff keeps those states on the
        // server and puts only a token in the page, so the first interactive render matches the
        // prerendered one without the whole board -- which grows with every checker -- crossing the
        // circuit.
        _persistSubscription = PersistedState.RegisterOnPersisting(PersistHandoffToken);

        await DashboardService.SubscribeToStateChangesAsync(OnStateChangedAsync);

        // A token that no longer resolves -- the page sat unconnected past the handoff's lifetime, or
        // another server picked up the circuit -- just falls back to reading the provider afresh.
        if (PersistedState.TryTakeFromJson<string>(HandoffTokenKey, out var token) && token is not null
            && Handoff.TryCollect(token, out var snapshot) && snapshot is not null)
        {
            _states = snapshot.States;
            _displayNames = snapshot.DisplayNames;
            MarkLoaded();
        }
        else
        {
            await LoadAsync();
        }

        // The first checker is selected by MarkLoaded rather than by a click, so it has not been
        // through the path that reads its uptime and fills the editors. Without this the panel opens
        // missing the columns and drafts that every later selection has.
        await SelectAsync(_selected);

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
            MarkLoaded();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Settles the board once its states are in hand, from either the provider or a snapshot.</summary>
    private void MarkLoaded()
    {
        _selected ??= _states.Keys.OrderBy(name => name).FirstOrDefault();
        _isLoading = false;
        Refresh();
    }

    /// <summary>
    /// At the end of prerendering, stashes the states just read on the server and persists only the
    /// token that collects them, so the interactive render can match this one without the board
    /// itself -- or a second read of every checker -- crossing the circuit.
    /// </summary>
    /// <remarks>
    /// The snapshot hands its dictionaries to the interactive render to own outright, no copy: the
    /// prerender's scope -- and the <see cref="IHealthieDashboardService"/> whose subscriptions could
    /// mutate them -- is torn down when the prerender response completes, which is before the circuit
    /// connects and collects them. So nothing writes to them once they have been handed over.
    /// <para>
    /// The token is persisted under one key per page, which optimises a single dashboard. A second
    /// dashboard on the same page overwrites the key and reads its state fresh instead -- correct,
    /// just without the handoff. There is no per-instance value stable across the separate prerender
    /// and interactive instances to key on (which is why <c>_traceId</c>, unique per instance, cannot
    /// serve) short of the .NET 9 declarative persistence API, which net8.0 does not have.
    /// </para>
    /// </remarks>
    private Task PersistHandoffToken()
    {
        var token = Handoff.Stash(new DashboardSnapshot(_states, _displayNames));
        PersistedState.PersistAsJson(HandoffTokenKey, token);

        return Task.CompletedTask;
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
                _now = DateTime.UtcNow;
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
    /// <remarks>
    /// Raised from whichever thread ran the check, so the work is marshalled onto the renderer's
    /// dispatcher: <see cref="ComponentBase.StateHasChanged"/> throws when called from anywhere
    /// else, and the throw is swallowed by the caller, which turns a broken render into silence.
    /// </remarks>
    private Task OnStateChangedAsync(string name, PulseCheckerState state) =>
        InvokeAsync(() =>
        {
            var previous = _states.TryGetValue(name, out var existing) ? existing : null;

            _states[name] = state;

            if (previous is not null)
            {
                RecordTransition(name, previous, state);
            }

            Refresh();
        });

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
        _events.Insert(0, new EventEntry(DateTime.UtcNow, tag, status, text));

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

        if (!string.IsNullOrWhiteSpace(_tagFilter))
        {
            filtered = filtered.Where(entry =>
                entry.Value.Tags.Contains(_tagFilter, StringComparer.OrdinalIgnoreCase));
        }

        // Narrowed from the side menu, and combined with the two above rather than replacing them.
        if (_navSection is { } section)
        {
            filtered = filtered.Where(entry => string.Equals(
                string.IsNullOrWhiteSpace(entry.Value.Group) ? UngroupedName : entry.Value.Group,
                section,
                StringComparison.OrdinalIgnoreCase));
        }

        // Pinned first, then by name. Pinning is only useful if it survives the sort.
        _filtered =
        [
            .. filtered
                .OrderByDescending(entry => entry.Value.IsPinned)
                .ThenBy(entry => DisplayNameOf(entry.Key), StringComparer.OrdinalIgnoreCase)
        ];

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

    /// <summary>
    /// The filtered checkers arranged by their group, with the ungrouped ones gathered last.
    /// </summary>
    /// <remarks>
    /// A checker has one group at most, so this is a partition: every checker appears once, under
    /// exactly one heading, and the headings' tallies add up to the list. Tags do not take part --
    /// several of them can be on one checker, which would put it under several headings and make
    /// the tallies count it twice.
    /// </remarks>
    private IEnumerable<CheckerGroup> GroupedRows() =>
        _filtered
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Value.Group) ? UngroupedName : entry.Value.Group!,
                     StringComparer.OrdinalIgnoreCase)
            .Select(group => new CheckerGroup(group.Key, [.. group]))
            // Ungrouped last: it is the leftovers, not a heading anyone chose.
            .OrderBy(group => group.Name == UngroupedName)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>One group's checkers, and the tallies its header shows.</summary>
    private sealed record CheckerGroup(string Name, List<KeyValuePair<string, PulseCheckerState>> Rows)
    {
        public int Healthy => Rows.Count(r => HealthOf(r.Value) == PulseCheckerHealth.Healthy);

        public int Suspicious => Rows.Count(r => HealthOf(r.Value) == PulseCheckerHealth.Suspicious);

        public int Failing => Rows.Count(r => HealthOf(r.Value) == PulseCheckerHealth.Unhealthy);

        /// <summary>The worst state in the group, which is what its header reports.</summary>
        public Status Worst =>
            Failing > 0 ? Status.Critical :
            Suspicious > 0 ? Status.Warning :
            Status.Ok;
    }

    private void ToggleGroup(string tag)
    {
        if (!_collapsedGroups.Remove(tag))
        {
            _collapsedGroups.Add(tag);
        }
    }

    private void ToggleView() => _asCards = !_asCards;

    private void ToggleGrouping()
    {
        _sectionByGroup = !_sectionByGroup;
        Refresh();
    }

    private void ToggleNav() => _navOpen = !_navOpen;

    /// <summary>
    /// The groups the menu lists, which are the ones in the store rather than the ones surviving the
    /// current filters.
    /// </summary>
    /// <remarks>
    /// Off <see cref="_states"/>, not off the filtered rows: a menu that dropped a group because the
    /// search box had narrowed it away would take away the means of getting back to it.
    /// </remarks>
    private IEnumerable<CheckerGroup> NavGroups() =>
        _states
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Value.Group) ? UngroupedName : entry.Value.Group!,
                     StringComparer.OrdinalIgnoreCase)
            .Select(group => new CheckerGroup(group.Key, [.. group]))
            .OrderBy(group => group.Name == UngroupedName)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

    private Task ShowEverythingAsync()
    {
        _navSection = null;
        Refresh();

        return Task.CompletedTask;
    }

    /// <summary>Narrows the list to one group, or back to everything when it is picked again.</summary>
    private Task ShowGroupAsync(string group)
    {
        _navSection = string.Equals(_navSection, group, StringComparison.OrdinalIgnoreCase) ? null : group;
        Refresh();

        return Task.CompletedTask;
    }

    private void OnTagFilterChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        _tagFilter = string.IsNullOrEmpty(value) ? null : value;
        Refresh();
    }

    private async Task OnRowKeyDown(KeyboardEventArgs args, string name)
    {
        if (args.Key is "Enter" or " ")
        {
            await SelectAsync(name);
        }
    }

    /// <summary>
    /// Selects a checker and reads anything the feature packages can add about it.
    /// </summary>
    /// <remarks>
    /// On selection rather than on the clock tick: uptime is a query over recorded segments and the
    /// board redraws every second.
    /// </remarks>
    private async Task SelectAsync(string? name)
    {
        _selected = name;
        _diagnosis = null;

        // The cron box follows the selection: left alone it would show one checker's expression
        // while Enter applied it to another.
        _cronDraft = _selectedState?.Schedule?.CronExpression;
        _scheduleError = null;

        await LoadUptimeAsync(name);
    }

    private void ToggleAbout() => _showAbout = !_showAbout;

    private void ToggleLog() => _showLog = !_showLog;

    private async Task TogglePinAsync(string name, bool isPinned)
    {
        await DashboardService.SetPinnedAsync(name, !isPinned);
        AddEvent("PIN", Status.Ok, $"{DisplayNameOf(name)} {(isPinned ? "unpinned" : "pinned")}");
    }

    /// <summary>Adds whatever is in the tag box to the selected checker.</summary>
    private async Task AddTagAsync()
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_tagDraft) || _selectedState is not { } state)
        {
            return;
        }

        var tags = new List<string>(state.Tags) { _tagDraft };
        _tagDraft = null;

        await DashboardService.SetTagsAsync(_selected, tags);
    }

    private async Task RemoveTagAsync(string tag)
    {
        if (_selected is null || _selectedState is not { } state)
        {
            return;
        }

        var tags = state.Tags.Where(t => !string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)).ToList();

        await DashboardService.SetTagsAsync(_selected, tags);

        // A tag that was being filtered on can be the one just removed, which would otherwise leave
        // the list filtered by something no checker carries any more.
        if (_tagFilter is not null && !AllTags.Contains(_tagFilter, StringComparer.OrdinalIgnoreCase))
        {
            _tagFilter = null;
            Refresh();
        }
    }

    private async Task OnTagDraftKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await AddTagAsync();
        }
    }

    /// <summary>Moves the selected checker into an existing group, or out of all of them.</summary>
    private async Task OnGroupChanged(ChangeEventArgs args)
    {
        if (_selected is null)
        {
            return;
        }

        var value = args.Value?.ToString();

        // The sentinel rather than a blank: a <select> cannot offer "type a new one", so choosing
        // it swaps the control for a text box instead of setting an empty group.
        if (value == NewGroupOption)
        {
            _isNamingGroup = true;
            return;
        }

        _isNamingGroup = false;
        await DashboardService.SetGroupAsync(_selected, value);
    }

    private async Task CommitNewGroupAsync()
    {
        if (_selected is null || string.IsNullOrWhiteSpace(_groupDraft))
        {
            _isNamingGroup = false;
            return;
        }

        var group = _groupDraft;
        _groupDraft = null;
        _isNamingGroup = false;

        await DashboardService.SetGroupAsync(_selected, group);
    }

    private async Task OnGroupDraftKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await CommitNewGroupAsync();
        }
        else if (args.Key == "Escape")
        {
            _groupDraft = null;
            _isNamingGroup = false;
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

    /// <summary>
    /// Applies what is in the cron box: a schedule, or none when it has been emptied.
    /// </summary>
    /// <remarks>
    /// On Enter rather than on every keystroke, because a half-typed expression is not a schedule
    /// and every attempt reschedules the checker. A refusal is shown where it was typed -- the
    /// schedulers disagree about cron dialects, so which rule was broken is the useful part, and the
    /// scheduler is asked before anything is stored.
    /// </remarks>
    private async Task ApplyCronAsync()
    {
        if (_selected is null)
        {
            return;
        }

        _scheduleError = null;

        var typed = _cronDraft?.Trim();

        try
        {
            if (string.IsNullOrEmpty(typed))
            {
                await DashboardService.SetScheduleAsync(_selected, null);
                AddEvent("CONF", Status.Paused, $"{DisplayNameOf(_selected)} back on its interval");

                return;
            }

            await DashboardService.SetScheduleAsync(_selected, PulseSchedule.Cron(typed));
            AddEvent("CONF", Status.Paused, $"{DisplayNameOf(_selected)} scheduled on '{typed}'");
        }
        catch (ArgumentException ex)
        {
            _scheduleError = ex.Message;
        }
    }

    private async Task OnCronKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                await ApplyCronAsync();
                break;
            case "Escape":
                _cronDraft = _selectedState?.Schedule?.CronExpression;
                _scheduleError = null;
                break;
        }
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

    /// <summary>
    /// What the rate column reads: a number of runs a minute, or the cron expression when the
    /// schedule is one.
    /// </summary>
    /// <remarks>
    /// Off <see cref="PulseCheckerState.EffectiveSchedule"/>, not off <c>Interval</c>. Interval is
    /// documented as ignored once a schedule is set, so a cron checker used to advertise a rate it
    /// was not running at -- and the aggregate summed that rate into its total.
    /// </remarks>
    private static string RateLabel(PulseCheckerState state)
    {
        var schedule = state.EffectiveSchedule;

        if (schedule.CronExpression is { } cron)
        {
            return cron;
        }

        var perMinute = 60d / schedule.Period!.Value.TotalSeconds;

        return perMinute >= 1 ? Math.Round(perMinute).ToString("0") : perMinute.ToString("0.0");
    }

    /// <summary>The unit under the rate, which a cron expression does not have.</summary>
    private static string RateUnit(PulseCheckerState state) =>
        state.EffectiveSchedule.IsCron ? "cron" : "per min";

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
        // A torn-down component must stop being handed state changes; the service outlives it when
        // the host routes to the dashboard inside its own layout. The other order is allowed too --
        // the circuit may dispose the service first -- and then this throws, because the lock it
        // takes is gone. Everything below stops the clock loop, so it has to run either way.
        try
        {
            await DashboardService.UnsubscribeFromStateChangesAsync(OnStateChangedAsync);
        }
        catch (ObjectDisposedException)
        {
            // The service went first, and took every handler with it.
        }

        await _disposing.CancelAsync();

        if (_clockLoop is not null)
        {
            await _clockLoop;
        }

        _disposing.Dispose();
        _loadLock.Dispose();
        _persistSubscription.Dispose();
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
