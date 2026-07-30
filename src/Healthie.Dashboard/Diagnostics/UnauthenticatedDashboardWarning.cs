using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthie.Dashboard.Diagnostics;

/// <summary>
/// Says so, once and loudly, when the dashboard is reachable without authenticating and its
/// controls are on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HealthieUIOptions.AllowMutations"/> defaults to <c>true</c> because the dashboard
/// exists to manage checkers and a read-only default would make every first run look broken. It is
/// not authorization and does not pretend to be: it decides which controls are rendered, for
/// everyone, and <c>RequireAuthorization</c> on the mapped endpoint is the other half. An
/// application that maps the dashboard and stops there has given anyone who can reach it the
/// ability to pause a checker or reset a failing streak.
/// </para>
/// <para>
/// Asked of the endpoint rather than assumed from the option, so an application that secured it --
/// by chaining <c>RequireAuthorization</c>, by an endpoint group, by its own attribute -- is not
/// warned at. A warning that fires on correctly secured applications gets filtered out, and then it
/// is not there for the one that needs it.
/// </para>
/// </remarks>
internal sealed class UnauthenticatedDashboardWarning(
    EndpointDataSource endpoints,
    HealthieUIOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<UnauthenticatedDashboardWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Nothing to say when the controls are not rendered: reaching the board then shows health
        // and changes nothing.
        if (!options.AllowMutations)
        {
            return Task.CompletedTask;
        }

        // After the application has started, because that is when the endpoints exist.
        lifetime.ApplicationStarted.Register(Warn);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Warn()
    {
        var dashboard = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(endpoint => string.Equals(
                "/" + endpoint.RoutePattern.RawText?.TrimStart('/'),
                StartupExtensions.DashboardPath,
                StringComparison.OrdinalIgnoreCase));

        // Not mapped at all, or already behind an authorization policy.
        if (dashboard is null || dashboard.Metadata.GetMetadata<IAuthorizeData>() is not null)
        {
            return;
        }

        Log.DashboardIsUnauthenticatedAndWritable(logger, StartupExtensions.DashboardPath);
    }
}
