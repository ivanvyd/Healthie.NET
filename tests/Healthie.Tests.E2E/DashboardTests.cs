using Microsoft.Playwright;

namespace Healthie.Tests.E2E;

/// <summary>
/// Drives the dashboard in a real browser against the real sample app, once per provider
/// combination. Everything here is asserted through the DOM a user actually gets, so a break in
/// the Blazor circuit, the CSS, or the render loop fails the test -- none of which a unit test sees.
/// </summary>
[Collection(nameof(BrowserCollection))]
public class DashboardTests(BrowserFixture browser)
{
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
        // Rows only appear once the checkers have been read, so this is the real readiness signal.
        await page.Locator(".hpm-row").First.WaitForAsync(new() { Timeout = 30_000 });
        return page;
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Dashboard_ListsEveryChecker_AndReportsNoBrowserErrors(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        Assert.Equal(14, await page.Locator(".hpm-row").CountAsync());
        Assert.Equal("HEALTHIE·PULSE", (await page.Locator(".hpm-wordmark").TextContentAsync())?.Trim());
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

        await page.Locator(".hpm-sel-name", new() { HasTextString = TargetChecker })
            .WaitForAsync(new() { Timeout = 10_000 });
        Assert.Equal(TargetChecker, (await page.Locator(".hpm-sel-name").TextContentAsync())?.Trim());
        Assert.Equal(3, await page.Locator(".hpm-stat").CountAsync());
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task Search_FiltersTheList_AndRestoresIt(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        await page.FillAsync(".hpm-search input", "no-such-checker");
        await page.Locator(".hpm-empty").WaitForAsync(new() { Timeout = 10_000 });
        Assert.Equal(0, await page.Locator(".hpm-row").CountAsync());

        await page.FillAsync(".hpm-search input", "");
        await page.Locator(".hpm-row").First.WaitForAsync(new() { Timeout = 10_000 });
        Assert.Equal(14, await page.Locator(".hpm-row").CountAsync());
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

        await page.Locator(".hpm-event").First.WaitForAsync(new() { Timeout = 20_000 });
        Assert.True(await page.Locator(".hpm-event").CountAsync() > 0);
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
        await page.Locator("#hpm-interval").WaitForAsync(new() { Timeout = 10_000 });
        await page.SelectOptionAsync("#hpm-interval", "Every5Minutes");
        await page.WaitForTimeoutAsync(1500);

        // Reloading drops every scrap of component state, so what comes back can only have come
        // from the state provider.
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle });
        await RowFor(page, TargetChecker).WaitForAsync(new() { Timeout = 30_000 });
        await RowFor(page, TargetChecker).ClickAsync();
        await page.Locator(".hpm-sel-name", new() { HasTextString = TargetChecker })
            .WaitForAsync(new() { Timeout = 10_000 });

        Assert.Equal("Every5Minutes", await page.Locator("#hpm-interval").InputValueAsync());
        browser.AssertNoErrors(page);
    }

    [Theory]
    [MemberData(nameof(Setups))]
    public async Task ThemeToggle_SwitchesBothWays(ProviderSetup setup)
    {
        await using var app = await SampleApp.StartAsync(setup, Ct);
        var page = await OpenDashboardAsync(app);

        Assert.Equal("dark", await page.Locator(".healthie-dashboard").GetAttributeAsync("data-hpm"));

        await page.Locator(".hpm-btn--theme").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        Assert.Equal("light", await page.Locator(".healthie-dashboard").GetAttributeAsync("data-hpm"));

        await page.Locator(".hpm-btn--theme").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        Assert.Equal("dark", await page.Locator(".healthie-dashboard").GetAttributeAsync("data-hpm"));
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
