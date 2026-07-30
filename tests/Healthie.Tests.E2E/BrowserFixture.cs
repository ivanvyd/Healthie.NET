using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace Healthie.Tests.E2E;

/// <summary>
/// One browser for the whole run. Launching Chromium costs about a second, which is not worth
/// paying per test; pages are still created fresh so no state leaks between them.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<IPage, List<string>> _errors = new();
    private readonly ConcurrentDictionary<IPage, IBrowserContext> _contexts = new();
    private readonly ConcurrentDictionary<IPage, object> _owners = new();
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async ValueTask InitializeAsync()
    {
        // Fetches the browser on a machine that has never run these, and no-ops once it is there,
        // so a contributor's first `dotnet test` just works. Deliberately without --with-deps: that
        // reaches for sudo to install system libraries, which is not this process's business. CI
        // installs those in its own step.
        var exitCode = Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Installing Chromium failed with exit code {exitCode}. Run 'pwsh playwright.ps1 install chromium --with-deps' from this project's build output.");
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });

        // Playwright's default is five seconds, which is tuned for a developer's machine driving one
        // page. Every assertion here waits on a Blazor Server round trip, and the release workflow
        // runs this suite straight after eleven hundred unit tests across two frameworks -- a loaded
        // runner made assertions that pass on every pull request fail twice during a release, which
        // reads as a broken feature rather than a busy machine. Raised once here rather than
        // sprinkled per call, so no future assertion has to remember.
        Assertions.SetDefaultExpectTimeout(20_000);
    }

    public async Task<IPage> NewPageAsync()
    {
        var browser = _browser ?? throw new InvalidOperationException("The browser is not running.");

        var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });

        // Recorded for every test and kept only for the ones that fail. A browser test that fails on
        // a runner and passes everywhere else is otherwise a stack trace and a guess: this leaves the
        // DOM, the network and a screenshot at the moment it gave up, which is the difference between
        // diagnosing it once and patching it three times.
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = false });

        var page = await context.NewPageAsync();
        _contexts[page] = context;

        // Keyed by the running test so a class that opens several pages closes only its own.
        if (TestContext.Current.Test is { } test)
        {
            _owners[page] = test;
        }

        return page;
    }

    /// <summary>
    /// Closes every page the current test opened, keeping a trace only if it failed.
    /// </summary>
    /// <remarks>
    /// Kept only on failure because a trace is a few megabytes and fifty-three passing ones are worth
    /// nothing. <see cref="TraceDirectory"/> is what CI uploads.
    /// </remarks>
    public async Task FinishCurrentTestAsync()
    {
        var test = TestContext.Current.Test;
        var failed = TestContext.Current.TestState?.Result == TestResult.Failed;
        var name = test?.TestDisplayName ?? "unknown-test";

        foreach (var (page, context) in _contexts.ToArray())
        {
            if (!ReferenceEquals(_owners.GetValueOrDefault(page), test))
            {
                continue;
            }

            _contexts.TryRemove(page, out _);
            _owners.TryRemove(page, out _);

            if (failed)
            {
                Directory.CreateDirectory(TraceDirectory);

                var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
                await context.Tracing.StopAsync(new() { Path = Path.Combine(TraceDirectory, $"{safe}.zip") });
            }
            else
            {
                await context.Tracing.StopAsync();
            }

            await context.CloseAsync();
        }
    }

    /// <summary>Where failure traces are written, and what CI collects.</summary>
    public static string TraceDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "playwright-traces");

    /// <summary>Records the console and page errors seen on a page, for <see cref="AssertNoErrors"/>.</summary>
    public void TrackErrors(IPage page, List<string> errors) => _errors[page] = errors;

    /// <summary>
    /// Fails the test if the browser reported any error. A Blazor circuit that throws keeps serving
    /// a page that looks right, so without this the DOM assertions would pass over a broken app.
    /// </summary>
    public void AssertNoErrors(IPage page)
    {
        if (_errors.TryGetValue(page, out var errors) && errors.Count > 0)
        {
            Assert.Fail("The browser reported errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}

[CollectionDefinition(nameof(BrowserCollection))]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>;
