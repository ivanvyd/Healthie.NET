using Healthie.Api.Routes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Healthie.Api.Diagnostics;

/// <summary>
/// Says so, once and loudly, when the endpoints that can change a checker are reachable without
/// authenticating.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddHealthieController</c> does not require authorization unless it is asked to, which is a
/// deliberate default -- this controller ships into someone else's MVC pipeline, and demanding a
/// policy it knows nothing about would break every application that maps it. The consequence is
/// that an application which maps it and does nothing else lets anyone stop a checker or clear a
/// failing streak. That is not a wrong default so much as one worth being told about.
/// </para>
/// <para>
/// Asked of the endpoints rather than of the flag that built them: the host may have applied
/// authorization some other way -- a group, middleware, an
/// <see cref="AuthorizeAttribute"/> of its own -- and a warning that cried wolf at a correctly
/// secured application would be scrolled past within a week, which costs more than saying nothing.
/// </para>
/// </remarks>
internal sealed class UnauthenticatedSurfaceWarning(
    EndpointDataSource endpoints,
    IHostApplicationLifetime lifetime,
    ILogger<UnauthenticatedSurfaceWarning> logger) : IHostedService
{
    /// <summary>The methods that change something, as opposed to reporting it.</summary>
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // After the application has started, because that is when the endpoints exist. Reading them
        // during StartAsync races the routing system that builds them.
        lifetime.ApplicationStarted.Register(Warn);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Warn()
    {
        var unprotected = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(IsHealthieRoute)
            .Where(Mutates)
            .Where(endpoint => !IsProtected(endpoint))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => route is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unprotected.Length == 0)
        {
            return;
        }

        Log.MutatingEndpointsAreUnauthenticated(logger, unprotected.Length, string.Join(", ", unprotected!));
    }

    /// <summary>
    /// Whether anything on this endpoint requires an authenticated caller.
    /// </summary>
    /// <remarks>
    /// Two shapes, because authorization arrives two ways and only one of them is metadata.
    /// <c>RequireAuthorization()</c> and <c>[Authorize]</c> put an <see cref="IAuthorizeData"/> on
    /// the endpoint. <c>AddHealthieController(requireAuthorization: true)</c> adds an
    /// <see cref="AuthorizeFilter"/> through an MVC convention, and that is an
    /// <c>IFilterMetadata</c> rather than an <see cref="IAuthorizeData"/> -- so looking only for the
    /// latter warned about the one configuration that had asked for authorization by name.
    /// </remarks>
    private static bool IsProtected(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null
        || endpoint.Metadata.GetMetadata<AuthorizeFilter>() is not null;

    private static bool IsHealthieRoute(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.RawText?.StartsWith(RoutesConstants.HealthieApiRoute, StringComparison.OrdinalIgnoreCase)
            ?? false;

    private static bool Mutates(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>() is { } methods
        && methods.HttpMethods.Any(method => MutatingMethods.Contains(method, StringComparer.OrdinalIgnoreCase));
}
