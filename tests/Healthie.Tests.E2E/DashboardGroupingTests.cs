using Microsoft.Playwright;

namespace Healthie.Tests.E2E;

/// <summary>
/// Drives the grouping, tagging, pinning, layout and event-log controls in a real browser.
/// </summary>
/// <remarks>
/// These assert what a user gets rather than what a method returns. That distinction matters here:
/// the dashboard once raised its renders from the wrong thread, which threw on every state change
/// and was swallowed, and the whole suite stayed green because a once-a-second clock tick redrew
/// the component anyway. <see cref="Dashboard_UpdatesFromAnEvent_WithoutRelyingOnTheClockTick"/>
/// is the test that would have caught it.
/// </remarks>
[Collection(nameof(BrowserCollection))]
public class DashboardGroupingTests(BrowserFixture browser)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Grouped and tagged by the sample, so it exercises both concepts at once.</summary>
    private const string TargetChecker = "Redis Cache";

    private const string TargetGroup = "Data Stores";

    /// <summary>The one checker in the sample that runs on a cron expression rather than an interval.</summary>
    private const string CronChecker = "TLS Certificate";

    private static ILocator RowFor(IPage page, string displayName) =>
        page.Locator(".hpm-row").Filter(new() { HasTextString = displayName });


    /// <summary>
    /// Selects a checker and waits for the panel to actually be showing it.
    /// </summary>
    /// <remarks>
    /// The click is a round trip to the server. Waiting on a panel control instead proves nothing --
    /// they are already on screen for whichever checker was selected on load, so a test that typed
    /// straight after the click edited the wrong checker and failed somewhere else entirely.
    /// </remarks>
    private static async Task SelectCheckerAsync(IPage page, string displayName)
    {
        await RowFor(page, displayName).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(displayName);
    }

    private static ILocator GroupFor(IPage page, string group) =>
        page.Locator(".hpm-group").Filter(new() { Has = page.Locator(".hpm-group-name", new() { HasTextString = group }) });

    /// <summary>The display name in the topmost row, which is what pinning is supposed to change.</summary>
    private static async Task<string> FirstRowNameAsync(IPage page) =>
        (await page.Locator(".hpm-row .hpm-row-title").First.TextContentAsync())?.Trim() ?? "";

    private async Task<IPage> OpenDashboardAsync(SampleApp app)
    {
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        browser.TrackErrors(page, errors);

        await page.GotoAsync(app.DashboardUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".hpm-row").First.WaitForAsync(new() { Timeout = 30_000 });
        return page;
    }

    /// <summary>
    /// The defaults declared in code have to survive the whole way to the screen: seeded into
    /// state on the first run, read back, and rendered.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_ShowsTheGroupAndTagsDeclaredInCode(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var row = RowFor(page, TargetChecker);

        await Assertions.Expect(row.Locator(".hpm-chip--group")).ToHaveTextAsync(TargetGroup);
        await Assertions.Expect(row.Locator(".hpm-chip", new() { HasTextString = "tier-1" })).ToBeVisibleAsync();
        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// A checker has one group, so grouping partitions the list: the rows under the headings add up
    /// to the rows without them, with nothing counted twice. Grouping by tags broke exactly this.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_WhenGrouped_ShowsEveryCheckerExactlyOnce(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        // The board opens grouped, so this is the state under test without touching anything.
        await page.Locator(".hpm-group").First.WaitForAsync();

        var groupedCount = await page.Locator(".hpm-row").CountAsync();
        Assert.Equal(1, await RowFor(page, TargetChecker).CountAsync());

        // The same checkers laid out flat: the button goes the other way now, which makes this the
        // comparison the test always wanted rather than a count taken before grouping was applied.
        await page.GetByRole(AriaRole.Button, new() { Name = "GROUP", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-group")).ToHaveCountAsync(0);

        Assert.Equal(groupedCount, await page.Locator(".hpm-row").CountAsync());
        Assert.Equal(1, await RowFor(page, TargetChecker).CountAsync());
        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The board opens sectioned by group rather than as one flat list, and the GROUP button
    /// reflects that rather than inviting a click that turns it off unannounced.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_OnOpen_IsSectionedByGroup(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await Assertions.Expect(page.Locator(".hpm-group").First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "GROUP", Exact = true }))
            .ToHaveAttributeAsync("aria-pressed", "true");

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The side menu narrows the list to one group, and says which one it is narrowed to.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_SideMenu_NarrowsTheListToOneGroup(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        // Asserted as invariants rather than against row totals taken up front: the board settles as
        // states arrive, so a count read on the way in is a race, and this failed once on a loaded
        // runner for exactly that reason.
        await Assertions.Expect(page.Locator(".hpm-group").First).ToBeVisibleAsync();

        var entry = page.Locator(".hpm-nav-item").Filter(new() { HasTextString = TargetGroup });
        await entry.ClickAsync();
        await Assertions.Expect(entry).ToHaveAttributeAsync("aria-current", "true");

        // Narrowed to one group: exactly one section, and every row in it carries that group.
        await Assertions.Expect(page.Locator(".hpm-group")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".hpm-group-name")).ToHaveTextAsync(TargetGroup);

        var groups = await page.Locator(".hpm-row .hpm-chip--group").AllTextContentsAsync();
        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.Equal(TargetGroup, group.Trim()));

        // Picking it again is the way back, so the menu cannot strand anyone in one group.
        await entry.ClickAsync();
        await Assertions.Expect(entry).Not.ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(page.Locator(".hpm-group").Nth(1)).ToBeVisibleAsync();

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// A cron expression typed on the board reaches the scheduler, and a bad one is refused without
    /// touching what is stored.
    /// </summary>
    /// <remarks>
    /// Ordered deliberately: the refusal is asserted first, so a passing "it applied" cannot be the
    /// result of the field simply ignoring everything typed into it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_CronEditor_AppliesAValidExpressionAndRefusesABadOne(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        // Waiting on the panel's title, not on the box: the box is already visible for whichever
        // checker was selected on load, so it tells you nothing about whether the click has landed.
        await RowFor(page, CronChecker).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(CronChecker);

        var box = page.Locator("#hpm-cron");
        var before = await box.InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(before));

        await box.FillAsync("99 99 * * *");
        await box.PressAsync("Enter");

        await Assertions.Expect(page.Locator(".hpm-field-error")).ToBeVisibleAsync();
        await Assertions.Expect(RowFor(page, CronChecker).Locator(".hpm-rate")).ToHaveTextAsync(before);

        await box.FillAsync("0 6 * * MON-FRI");
        await box.PressAsync("Enter");

        await Assertions.Expect(page.Locator(".hpm-field-error")).ToHaveCountAsync(0);
        await Assertions.Expect(RowFor(page, CronChecker).Locator(".hpm-rate")).ToHaveTextAsync("0 6 * * MON-FRI");

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The interval picker is inert while a cron expression is in force, because the schedule
    /// overrides it -- a live picker would store a value nothing reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_ForACronChecker_DisablesTheIntervalPicker(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await RowFor(page, CronChecker).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(CronChecker);
        await Assertions.Expect(page.Locator("#hpm-interval")).ToBeDisabledAsync();

        await RowFor(page, TargetChecker).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(TargetChecker);
        await Assertions.Expect(page.Locator("#hpm-interval")).ToBeEnabledAsync();

        browser.AssertNoErrors(page);
    }

    /// <summary>A group's header reports what is inside it, so its tallies must add up to that.</summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_GroupHeader_TalliesMatchTheRowsInsideIt(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var group = GroupFor(page, TargetGroup);
        await group.WaitForAsync();

        var rows = await group.Locator(".hpm-row").CountAsync();
        var tallies = await group.Locator(".hpm-gs").AllTextContentsAsync();
        var counted = tallies.Sum(t => int.Parse(t.Trim()));

        Assert.Equal(rows, counted);
        Assert.Equal(rows.ToString(), (await group.Locator(".hpm-group-count").TextContentAsync())?.Trim());
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_CollapsingAGroup_HidesItsRowsAndLeavesTheOthers(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var group = GroupFor(page, TargetGroup);
        await group.WaitForAsync();

        var inside = await group.Locator(".hpm-row").CountAsync();
        var total = await page.Locator(".hpm-row").CountAsync();
        Assert.True(inside > 0);

        await group.Locator(".hpm-group-head").ClickAsync();

        // Awaited rather than counted on the spot: the click is a round-trip to the server and back
        // before anything re-renders.
        await Assertions.Expect(group.Locator(".hpm-row")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".hpm-row")).ToHaveCountAsync(total - inside);
        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// A tag is on several checkers across several groups, so filtering by one must narrow the list
    /// without losing anything that carries it.
    /// </summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_FilteringByATag_ShowsOnlyTheCheckersCarryingIt(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var total = await page.Locator(".hpm-row").CountAsync();

        await page.Locator(".hpm-tag-filter").SelectOptionAsync("tier-1");
        await Assertions.Expect(page.Locator(".hpm-row")).Not.ToHaveCountAsync(total);

        var shown = await page.Locator(".hpm-row").CountAsync();
        Assert.True(shown > 0, "filtering by a tag the sample uses should leave something on screen");

        // Every row left has to carry the tag, which is the only claim the filter makes.
        Assert.Equal(shown, await page.Locator(".hpm-row").Filter(new() { Has = page.Locator(".hpm-chip", new() { HasTextString = "tier-1" }) }).CountAsync());
        browser.AssertNoErrors(page);
    }

    /// <summary>Pinning is stored, so it must outlive the page that did it.</summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_PinningAChecker_SortsItFirstAndSurvivesAReload(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        Assert.NotEqual(TargetChecker, await FirstRowNameAsync(page));

        await RowFor(page, TargetChecker).GetByRole(AriaRole.Button, new() { Name = $"Pin {TargetChecker}" }).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-row").First.Locator(".hpm-pin-mark")).ToBeVisibleAsync();

        Assert.Equal(TargetChecker, await FirstRowNameAsync(page));

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".hpm-row").First.WaitForAsync();

        Assert.Equal(TargetChecker, await FirstRowNameAsync(page));
        browser.AssertNoErrors(page);
    }

    /// <summary>A tag added here is stored, so it must still be there after a reload.</summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_AddingATag_ShowsItOnTheRowAndSurvivesAReload(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await RowFor(page, TargetChecker).ClickAsync();
        await page.GetByLabel("Add a tag").FillAsync("e2e-added");
        await page.GetByRole(AriaRole.Button, new() { Name = "ADD" }).ClickAsync();

        var tagged = page.Locator(".hpm-row").Filter(new() { HasTextString = TargetChecker })
            .Locator(".hpm-chip", new() { HasTextString = "e2e-added" });
        await Assertions.Expect(tagged).ToBeVisibleAsync();

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".hpm-row").First.WaitForAsync();

        await Assertions.Expect(page.Locator(".hpm-row").Filter(new() { HasTextString = TargetChecker })
            .Locator(".hpm-chip", new() { HasTextString = "e2e-added" })).ToBeVisibleAsync();
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_SwitchingToCards_KeepsEveryCheckerOnScreen(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var total = await page.Locator(".hpm-row").CountAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Card view" }).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-list--cards")).ToBeVisibleAsync();

        Assert.Equal(total, await page.Locator(".hpm-row").CountAsync());
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_ExpandingTheEventLog_OpensItAndClosesAgain(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        // The log starts empty and fills as checks report in, so give it something to show.
        await RowFor(page, TargetChecker).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "RUN NOW" }).ClickAsync();
        await page.Locator(".hpm-log-body .hpm-event").First.WaitForAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand the event log" }).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-modal")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".hpm-log-table")).ToBeVisibleAsync();

        // Dismissed from the dimmed edge, where a person clicks. A default click lands in the
        // middle of the backdrop, which is precisely where the modal is.
        await page.Locator(".hpm-backdrop").ClickAsync(new() { Position = new() { X = 8, Y = 8 } });
        await Assertions.Expect(page.Locator(".hpm-modal")).Not.ToBeVisibleAsync();
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_LegendAndAbout_OpensBehindTheHeaderIconRatherThanTakingTheSidebar(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await Assertions.Expect(page.Locator(".hpm-popover")).Not.ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Legend and about" }).ClickAsync();

        await Assertions.Expect(page.Locator(".hpm-popover")).ToBeVisibleAsync();
        // The version comes from the assembly; a Razor slip once rendered it as literal text.
        await Assertions.Expect(page.Locator(".hpm-popover-head .hpm-tag")).Not.ToHaveTextAsync("v@PackageVersion");
        browser.AssertNoErrors(page);
    }

    /// <summary>Every time on the dashboard is UTC, and says so.</summary>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_ShowsUtcRatherThanTheServersLocalTime(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var clock = (await page.Locator(".hpm-clock").TextContentAsync())?.Trim() ?? "";

        Assert.Contains("UTC", clock);
        Assert.DoesNotContain("LOCAL", clock);

        // Within a couple of minutes of the real UTC clock, which a local time would not be unless
        // the machine running this happens to sit on UTC.
        var match = System.Text.RegularExpressions.Regex.Match(clock, @"(\d{2}):(\d{2}):(\d{2})");
        Assert.True(match.Success, $"no clock found in '{clock}'");
        var shown = new TimeSpan(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));

        // Subtracted as TimeSpans, and wrapped by hand. TimeOnly's own subtraction never returns a
        // negative: a clock a fraction of a second behind comes back as nearly twenty-four hours
        // ahead, which reads as a colossal drift when the two agree to the second.
        var drift = shown - DateTime.UtcNow.TimeOfDay;
        if (drift > TimeSpan.FromHours(12)) drift -= TimeSpan.FromDays(1);
        if (drift < TimeSpan.FromHours(-12)) drift += TimeSpan.FromDays(1);

        Assert.True(drift.Duration() < TimeSpan.FromMinutes(2), $"clock read {clock}, UTC is {DateTime.UtcNow:HH:mm:ss}");
        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The regression guard for the render path.
    /// </summary>
    /// <remarks>
    /// Asserted against the app's own log rather than the DOM, on purpose. The dashboard raises
    /// renders from whichever thread ran the check; getting that wrong throws, and the throw is
    /// swallowed by the subscriber's error handling and logged. Nothing visible breaks, because the
    /// once-a-second clock tick redraws the component and picks the change up on its way past --
    /// which is why every browser assertion here stayed green while it was broken, and why this one
    /// reads the log instead.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DashboardTests.Setups), MemberType = typeof(DashboardTests))]
    public async Task Dashboard_RendersStateChanges_WithoutThrowingOffTheRenderersThread(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        // Every checker at once, so a subscriber that throws has plenty of chances to.
        await page.GetByRole(AriaRole.Button, new() { Name = "RUN ALL" }).ClickAsync();
        await page.Locator(".hpm-log-body .hpm-event").First.WaitForAsync();
        await page.WaitForTimeoutAsync(2000);

        var output = app.OutputSoFar();

        Assert.DoesNotContain("not associated with the Dispatcher", output);
        Assert.DoesNotContain("A subscriber threw", output);
        browser.AssertNoErrors(page);
    }
}
