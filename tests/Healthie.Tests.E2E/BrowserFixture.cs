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
        Program.Main(["install", "chromium", "--with-deps"]);

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
