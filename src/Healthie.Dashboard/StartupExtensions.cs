using Healthie.Dashboard.Diagnostics;
using Healthie.Dashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace Healthie.Dashboard;

/// <summary>
/// Extension methods for registering the Healthie.NET UI dashboard with dependency injection
/// and mapping it to an endpoint.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// The fixed path where the Healthie.NET dashboard is served.
    /// </summary>
    public const string DashboardPath = "/healthie/dashboard";

    /// <summary>
    /// Registers the Healthie.NET UI dashboard services with the service collection.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">
    /// An optional action to configure <see cref="HealthieUIOptions"/>.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddHealthieUI(
        this IServiceCollection services,
        Action<HealthieUIOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HealthieUIOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        // One per application: it bridges each prerender to its interactive circuit, which are
        // separate scopes on the same server, so the store that connects them must outlive both.
        services.AddSingleton<DashboardStateHandoff>();
        services.AddScoped<IHealthieDashboardService, HealthieDashboardService>();
        services.AddScoped<HealthieThemeState>();

        // Says so at startup if the board ends up reachable without authenticating while its
        // controls are on. TryAdd because calling this twice should not warn twice.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, UnauthenticatedDashboardWarning>());

        return services;
    }

    /// <summary>
    /// Maps the Healthie.NET UI dashboard to the <c>/healthie/dashboard</c> endpoint.
    /// This is intended for non-Blazor applications. For Blazor apps, use the
    /// <c>&lt;HealthieDashboard /&gt;</c> component directly in a Razor page with
    /// <c>@page "/healthie/dashboard"</c>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> for further endpoint configuration
    /// (e.g. <c>.RequireAuthorization()</c>).
    /// </returns>
    public static IEndpointConventionBuilder MapHealthieUI(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGet(DashboardPath, (HttpContext context) =>
        {
            var opts = context.RequestServices.GetService<HealthieUIOptions>();

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(BuildPage(opts?.DashboardTitle));
        });
    }

    /// <summary>
    /// The host page that renders the dashboard component.
    /// </summary>
    /// <remarks>
    /// Separate from the endpoint so it can be asserted on without a host. This is the one place in
    /// the library that builds HTML as a string instead of letting Razor build it, which is why the
    /// title is encoded here by hand -- Razor would have done it everywhere else.
    /// </remarks>
    internal static string BuildPage(string? dashboardTitle)
    {
            // The title is a host's setting, not a visitor's, so this matters only when a host
            // builds it from something it did not write -- a per-tenant label, a value out of a
            // database -- but that is a normal thing to do, and nothing here would have stopped it
            // closing the title element and opening a script one.
            var title = WebUtility.HtmlEncode(dashboardTitle ?? "System Health");

            var html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                    <title>{{title}}</title>
                    <link href="_content/Healthie.NET.Dashboard/healthie.css" rel="stylesheet" />
                    <base href="/" />
                    <!--
                        This page is the dashboard and nothing else, so the browser's default 8px
                        body margin only draws a pale frame around a dark tool. Scoped to this
                        page rather than put in healthie.css, which is also loaded by hosts that
                        embed the component in a page of their own and own their own body.
                    -->
                    <style>html, body { margin: 0; padding: 0; }</style>
                </head>
                <body>
                    <component type="typeof(Healthie.Dashboard.Components.HealthieDashboard)" render-mode="ServerPrerendered" />
                    <script src="_framework/blazor.server.js"></script>
                </body>
                </html>
                """;

            return html;
    }
}
