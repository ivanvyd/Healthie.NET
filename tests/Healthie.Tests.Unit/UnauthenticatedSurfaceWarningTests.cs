using Healthie.Abstractions.Scheduling;
using Healthie.Api;
using Healthie.Dashboard;
using Healthie.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthie.Tests.Unit;

/// <summary>
/// The startup warning for surfaces that can change a checker without anyone authenticating.
/// </summary>
/// <remarks>
/// Driven through a real host so the endpoints exist and carry the metadata the warning reads.
/// Asserting on the option instead would only restate the code: the point is that the check follows
/// what was actually applied, including authorization the host added its own way.
/// </remarks>
public class UnauthenticatedSurfaceWarningTests
{
    /// <summary>Captures what was logged, so a warning can be asserted on rather than eyeballed.</summary>
    private sealed class CapturingProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capturing(this);

        public void Dispose()
        {
        }

        private void Add(LogLevel level, string message)
        {
            lock (_entries)
            {
                _entries.Add((level, message));
            }
        }

        private sealed class Capturing(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Add(logLevel, formatter(state, exception));
        }
    }

    private static async Task<IReadOnlyList<(LogLevel Level, string Message)>> RunAsync(
        Action<IServiceCollection> configureServices,
        Action<WebApplication> configureApp)
    {
        var capture = new CapturingProvider();

        var builder = WebApplication.CreateBuilder();
        // Port 0 lets the OS pick a free one, so parallel tests cannot collide and no
        // TestHost package is needed to get real endpoints built.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(capture);

        builder.Services.AddHealthie(typeof(UnauthenticatedSurfaceWarningTests).Assembly);
        builder.Services.AddAuthorization();
        configureServices(builder.Services);

        var app = builder.Build();
        configureApp(app);

        await app.StartAsync();
        await app.StopAsync();
        await app.DisposeAsync();

        return capture.Entries;
    }

    private static bool Warned(IReadOnlyList<(LogLevel Level, string Message)> entries, string fragment) =>
        entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public async Task AnUngatedApi_WarnsAboutTheEndpointsThatCanChangeAChecker()
    {
        var entries = await RunAsync(
            services => services.AddHealthieController(),
            app => app.MapControllers());

        Assert.True(
            Warned(entries, "can change a pulse checker are reachable"),
            "mapping the controller with no authorization should have warned");
    }

    /// <summary>
    /// The warning follows what was applied, not the flag that was passed, so a host that required
    /// authorization must not be warned at.
    /// </summary>
    [Fact]
    public async Task AnApiThatRequiresAuthorization_IsNotWarnedAt()
    {
        var entries = await RunAsync(
            services => services.AddHealthieController(requireAuthorization: true),
            app => app.MapControllers());

        Assert.False(Warned(entries, "can change a pulse checker are reachable"));
    }

    /// <summary>
    /// And a host that secured the endpoints its own way -- not through the flag -- is also not
    /// warned at. This is the case that makes the check worth doing over the endpoints.
    /// </summary>
    [Fact]
    public async Task AnApiSecuredByTheHostItself_IsNotWarnedAt()
    {
        var entries = await RunAsync(
            services => services.AddHealthieController(),
            app => app.MapControllers().RequireAuthorization());

        Assert.False(Warned(entries, "can change a pulse checker are reachable"));
    }

    [Fact]
    public async Task AnUngatedDashboardWithControlsOn_Warns()
    {
        var entries = await RunAsync(
            services => services.AddHealthieUI(),
            app => app.MapHealthieUI());

        Assert.True(
            Warned(entries, "controls are on"),
            "mapping a writable dashboard with no authorization should have warned");
    }

    [Fact]
    public async Task AReadOnlyDashboard_IsNotWarnedAt()
    {
        var entries = await RunAsync(
            services => services.AddHealthieUI(options => options.AllowMutations = false),
            app => app.MapHealthieUI());

        Assert.False(Warned(entries, "controls are on"));
    }

    [Fact]
    public async Task ADashboardBehindAuthorization_IsNotWarnedAt()
    {
        var entries = await RunAsync(
            services => services.AddHealthieUI(),
            app => app.MapHealthieUI().RequireAuthorization());

        Assert.False(Warned(entries, "controls are on"));
    }

    /// <summary>
    /// The reads are not the concern: seeing health without authenticating is a choice an operator
    /// can reasonably make, and warning about it would drown the case that matters.
    /// </summary>
    [Fact]
    public async Task TheWarning_NamesOnlyTheMutatingRoutes()
    {
        var entries = await RunAsync(
            services => services.AddHealthieController(),
            app => app.MapControllers());

        var warning = entries.Single(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("can change a pulse checker", StringComparison.Ordinal));

        Assert.Contains("trigger", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reset", warning.Message, StringComparison.OrdinalIgnoreCase);

        // "intervals" is the read-only listing endpoint.
        Assert.DoesNotContain("healthie/intervals", warning.Message, StringComparison.OrdinalIgnoreCase);
    }
}
