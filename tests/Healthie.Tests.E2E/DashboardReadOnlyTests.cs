using Microsoft.Playwright;

namespace Healthie.Tests.E2E;

/// <summary>
/// Drives <c>HealthieUIOptions.AllowMutations</c> in a real browser, from both sides.
/// </summary>
/// <remarks>
/// One setup rather than every provider combination, deliberately: the option decides what gets
/// rendered, and nothing about it reaches the scheduler or the state store. Running it against
/// Quartz as well would cost a second app start to re-assert the same markup.
/// <para>
/// Each assertion here has a mirror in <see cref="Dashboard_WithMutationsOn_ShowsThoseSameControls"/>.
/// On their own the read-only assertions pass just as happily against a dashboard that renders no
/// controls at all, or against a typo in a selector -- the pair is what pins the option to the
/// behaviour rather than to one of its two outcomes.
/// </para>
/// </remarks>
[Collection(nameof(BrowserCollection))]
public class DashboardReadOnlyTests(BrowserFixture browser)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ProviderSetup Setup = new("Timer", UseCosmos: false);

    /// <summary>
    /// Everything on the board that changes a checker rather than reporting one.
    /// </summary>
    private static readonly (string Selector, string What)[] MutatingControls =
    [
        (".hpm-row-actions", "the per-row pin, run and pause buttons"),
        (".hpm-sel-actions", "RUN NOW / PAUSE / RESET"),
        (".hpm-fields", "the interval and threshold editors"),
        (".hpm-tags-editor", "the group and tag editors"),
        ("button:text-is('RUN ALL')", "RUN ALL"),
        ("[aria-label='Start all checkers']", "start all"),
        ("[aria-label='Pause all checkers']", "pause all"),
    ];

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

    [Fact]
    public async Task Dashboard_WithMutationsOff_HidesEveryControlThatChangesAChecker()
    {
        await using var app = await SampleApp.StartAsync(Setup, Ct, allowMutations: false);
        var page = await OpenDashboardAsync(app);

        foreach (var (selector, what) in MutatingControls)
        {
            Assert.True(
                await page.Locator(selector).CountAsync() == 0,
                $"{what} ({selector}) is on a dashboard that does not allow mutations.");
        }

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The mirror. Without it, every assertion above would still pass if the controls had simply
    /// been deleted, or if each selector were misspelled.
    /// </summary>
    [Fact]
    public async Task Dashboard_WithMutationsOn_ShowsThoseSameControls()
    {
        await using var app = await SampleApp.StartAsync(Setup, Ct, allowMutations: true);
        var page = await OpenDashboardAsync(app);

        foreach (var (selector, what) in MutatingControls)
        {
            Assert.True(
                await page.Locator(selector).CountAsync() > 0,
                $"{what} ({selector}) is missing from a dashboard that allows mutations.");
        }

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// Read-only has to mean reporting-only, not broken: the point of the option is a board that a
    /// wide audience can be shown, which it is not if it shows them nothing.
    /// </summary>
    [Fact]
    public async Task Dashboard_WithMutationsOff_StillReportsEverything()
    {
        await using var app = await SampleApp.StartAsync(Setup, Ct, allowMutations: false);
        var page = await OpenDashboardAsync(app);

        // A check has to land before there is any history to draw.
        await page.Locator(".hpm-blips").First.WaitForAsync(new() { Timeout = 30_000 });

        Assert.True(await page.Locator(".hpm-row").CountAsync() > 0, "no checkers are listed");
        Assert.True(await page.Locator(".hpm-stats").CountAsync() > 0, "the selected checker's stats are missing");
        Assert.Equal(1, await page.Locator(".hpm-log-body").CountAsync());

        // The controls that only change what the viewer sees are not mutations, so they stay.
        Assert.Equal(1, await page.Locator("[aria-label='Search checkers']").CountAsync());
        Assert.Equal(1, await page.Locator("button:text-is('GROUP')").CountAsync());
        Assert.Equal(1, await page.Locator("[aria-label='Legend and about']").CountAsync());
        Assert.Equal(1, await page.Locator("[aria-label='Expand the event log']").CountAsync());

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The rows and the column header share one grid track definition, so the actions column has to
    /// leave both or neither -- drop it from one alone and every row sits one column out of step
    /// with its heading.
    /// </summary>
    /// <remarks>
    /// Track counts rather than widths: a row carries a 3px left border and a 1px right one that
    /// the header does not, so their flexible tracks resolve about two pixels apart. That is true
    /// of the six-column layout as well, invisible at a couple of pixels, and nothing to do with
    /// this option -- asserting the widths matched would only pin down a coincidence.
    /// </remarks>
    [Fact]
    public async Task Dashboard_WithMutationsOff_DropsTheActionsColumnFromRowsAndHeaderAlike()
    {
        await using var app = await SampleApp.StartAsync(Setup, Ct, allowMutations: false);
        var page = await OpenDashboardAsync(app);

        var tracks = await page.EvaluateAsync<int[]>(
            @"() => ['.hpm-list-head', '.hpm-row'].map(
                  s => getComputedStyle(document.querySelector(s)).gridTemplateColumns.split(' ').length)");

        Assert.Equal(tracks[0], tracks[1]);
        Assert.Equal(5, tracks[0]);

        browser.AssertNoErrors(page);
    }

    /// <summary>
    /// The mirror again: five tracks only means something if the board that has the controls has
    /// six.
    /// </summary>
    [Fact]
    public async Task Dashboard_WithMutationsOn_KeepsTheActionsColumn()
    {
        await using var app = await SampleApp.StartAsync(Setup, Ct, allowMutations: true);
        var page = await OpenDashboardAsync(app);

        var tracks = await page.EvaluateAsync<int[]>(
            @"() => ['.hpm-list-head', '.hpm-row'].map(
                  s => getComputedStyle(document.querySelector(s)).gridTemplateColumns.split(' ').length)");

        Assert.Equal(tracks[0], tracks[1]);
        Assert.Equal(6, tracks[0]);

        browser.AssertNoErrors(page);
    }
}
