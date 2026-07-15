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
    }

    public async Task<IPage> NewPageAsync()
    {
        var browser = _browser ?? throw new InvalidOperationException("The browser is not running.");
        return await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
    }

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
