using Healthie.Dashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IHealthieDashboardService, HealthieDashboardService>();
        services.AddScoped<HealthieThemeState>();

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
            var title = opts?.DashboardTitle ?? "System Health";

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

            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(html);
        });
    }
}
