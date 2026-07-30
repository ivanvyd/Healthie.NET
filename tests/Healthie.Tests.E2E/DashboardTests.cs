using Microsoft.Playwright;

namespace Healthie.Tests.E2E;

/// <summary>
/// Drives the dashboard in a real browser against the real sample app, once per provider
/// combination. Everything here is asserted through the DOM a user actually gets, so a break in
/// the Blazor circuit, the CSS, or the render loop fails the test -- none of which a unit test sees.
/// </summary>
[Collection(nameof(BrowserCollection))]
public class DashboardTests(BrowserFixture browser) : IAsyncDisposable
{
    /// <summary>Closes the pages this test opened, keeping a trace behind if it failed.</summary>
    public async ValueTask DisposeAsync() => await browser.FinishCurrentTestAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every provider combination that runs without external infrastructure, plus the CosmosDB ones
    /// when <c>HEALTHIE_TEST_COSMOS</c> supplies a connection string.
    /// </summary>
    public static TheoryData<ProviderSetup> Setups
    {
        get
        {
            var data = new TheoryData<ProviderSetup>
            {
                new ProviderSetup("Timer", UseCosmos: false),
                new ProviderSetup("Quartz", UseCosmos: false),
            };

            if (SampleApp.CosmosConnectionString is not null)
            {
                data.Add(new ProviderSetup("Timer", UseCosmos: true));
                data.Add(new ProviderSetup("Quartz", UseCosmos: true));
            }

            return data;
        }
    }

    /// <summary>
    /// A checker from the sample, addressed by the name a user sees. The list re-renders as checks
    /// report in, so anything positional ("the first row") silently targets whatever happens to be
    /// there at click time.
    /// </summary>
    private const string TargetChecker = "Redis Cache";

    /// <summary>How many pulse checkers the sample declares.</summary>
    /// <remarks>
    /// Asserted rather than ignored: the board losing a row is exactly the kind of regression a
    /// "some rows are present" check waves through.
    /// </remarks>
    internal const int CheckerCount = 14;

    private static ILocator RowFor(IPage page, string displayName) =>
        page.Locator(".hpm-row").Filter(new() { HasTextString = displayName });

    private async Task<IPage> OpenDashboardAsync(SampleApp app)
    {
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        browser.TrackErrors(page, errors);

        await page.GotoAsync(app.DashboardUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForTheBoardAsync(page);
        return page;
    }

    /// <summary>
    /// Waits until the board is showing every checker, which is what "loaded" means here.
    /// </summary>
    /// <remarks>
    /// Rows arrive as the checkers are read, so waiting for the first one only proves the list has
    /// started. A count taken at that moment can catch it half-built -- and a count is the thing
    /// most of these tests then compare against, which is how a suite that passes on a developer's
    /// machine fails on a loaded runner. Waiting for the full set here is what makes reading a
    /// count downstream safe at all.
    /// </remarks>
    internal static Task WaitForTheBoardAsync(IPage page) =>
        Assertions.Expect(page.Locator(".hpm-row")).ToHaveCountAsync(CheckerCount, new() { Timeout = 30_000 });

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Dashboard_ListsEveryChecker_AndReportsNoBrowserErrors(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await Assertions.Expect(page.Locator(".hpm-row")).ToHaveCountAsync(CheckerCount);
        await Assertions.Expect(page.Locator(".hpm-wordmark")).ToHaveTextAsync("HEALTHIE·PULSE");
        browser.AssertNoErrors(page);
    }

    // The dashboard is event-driven: nothing polls. If the circuit or the render loop is broken it
    // still paints once from the prerender and then silently freezes, which only a live check catches.
    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Dashboard_KeepsRendering_AfterTheCircuitConnects(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var before = await page.Locator(".hpm-clock").TextContentAsync();
        await page.WaitForTimeoutAsync(2500);
        var after = await page.Locator(".hpm-clock").TextContentAsync();

        Assert.NotEqual(before, after);
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task SelectingAChecker_ShowsItsDetail(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await RowFor(page, TargetChecker).ClickAsync();

        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(TargetChecker);

        // A count would be a timing assertion, not a detail one: the sample installs the uptime
        // package, whose 24H cell appears once a segment has been recorded and whose WORST cell
        // appears only after an outage. So: the three that are always there, and nothing unexpected.
        // Scoped to the stats grid: the same label class dresses the GROUP and TAGS editors below it.
        var labels = await page.Locator(".hpm-stats .hpm-stat-label").AllTextContentsAsync();
        var trimmed = labels.Select(label => label.Trim()).ToList();

        Assert.Contains("UPTIME", trimmed);
        Assert.Contains("FAILS", trimmed);
        Assert.Contains("STATE", trimmed);
        Assert.Empty(trimmed.Except(["UPTIME", "FAILS", "STATE", "24H", "WORST"]));

        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Search_FiltersTheList_AndRestoresIt(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await page.FillAsync(".hpm-search input", "no-such-checker");
        await Assertions.Expect(page.Locator(".hpm-row")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".hpm-empty")).ToBeVisibleAsync();

        await page.FillAsync(".hpm-search input", "");
        await Assertions.Expect(page.Locator(".hpm-row")).ToHaveCountAsync(CheckerCount);
        browser.AssertNoErrors(page);
    }

    // Running a check has to reach the scheduler and come back as a state change the event log
    // shows, which is the whole path from the browser to the provider and back.
    [Theory]
    [MemberData(nameof(Setups))]
    public async Task RunAll_ProducesEventLogEntries(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await page.Locator(".hpm-btn", new() { HasTextString = "RUN ALL" }).ClickAsync();

        await Assertions.Expect(page.Locator(".hpm-event").First).ToBeVisibleAsync();
        browser.AssertNoErrors(page);
    }

    // Changing the interval writes through the provider, so this is where a broken state provider
    // shows up: the value would not survive the round trip.
    [Theory]
    [MemberData(nameof(Setups))]
    public async Task ChangingTheInterval_PersistsThroughTheStateProvider(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await RowFor(page, TargetChecker).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(TargetChecker);
        await page.SelectOptionAsync("#hpm-interval", "Every5Minutes");

        // The event log entry is written once the write to the provider has returned, so waiting for
        // it waits for the round trip. A fixed sleep only guessed at how long that takes, and guessed
        // from a developer's machine.
        await Assertions.Expect(page.Locator(".hpm-event").Filter(new() { HasTextString = "interval set to" }).First)
            .ToBeVisibleAsync();

        // Reloading drops every scrap of component state, so what comes back can only have come
        // from the state provider.
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForTheBoardAsync(page);
        await RowFor(page, TargetChecker).ClickAsync();
        await Assertions.Expect(page.Locator(".hpm-sel-name")).ToHaveTextAsync(TargetChecker);

        await Assertions.Expect(page.Locator("#hpm-interval")).ToHaveValueAsync("Every5Minutes");
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task ThemeToggle_SwitchesBothWays(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        var board = page.Locator(".healthie-dashboard");

        await Assertions.Expect(board).ToHaveAttributeAsync("data-hpm", "dark");

        await page.Locator(".hpm-btn--theme").ClickAsync();
        await Assertions.Expect(board).ToHaveAttributeAsync("data-hpm", "light");

        await page.Locator(".hpm-btn--theme").ClickAsync();
        await Assertions.Expect(board).ToHaveAttributeAsync("data-hpm", "dark");
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Dashboard_DoesNotScrollSideways_OnANarrowViewport(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await page.SetViewportSizeAsync(375, 900);
        await page.WaitForTimeoutAsync(600);

        var overflows = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");

        Assert.False(overflows, "The dashboard scrolls horizontally at 375px.");
        browser.AssertNoErrors(page);
    }
}
