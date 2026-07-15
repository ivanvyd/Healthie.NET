using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Mcp;

/// <summary>
/// Extension methods for registering the Healthie.NET MCP server with dependency injection and
/// mapping it to an endpoint.
/// </summary>
public static class StartupExtensions
{
    /// <summary>The path the MCP endpoint is served from by default.</summary>
    public const string McpPath = "/healthie/mcp";

    /// <summary>
    /// Registers an MCP server exposing pulse checker health, so an AI agent can inspect the health
    /// of your services.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">An optional action to configure <see cref="HealthieMcpOptions"/>.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Only read-only tools are exposed unless <see cref="HealthieMcpOptions.AllowMutations"/> is
    /// turned on. Map the endpoint with <see cref="MapHealthieMcp"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddHealthieMcp(
        this IServiceCollection services,
        Action<HealthieMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HealthieMcpOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var builder = services
            .AddMcpServer()
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<HealthieTools>();

        if (options.AllowMutations)
        {
            builder.WithTools<HealthieActionTools>();
        }

        return services;
    }

    /// <summary>
    /// Maps the Healthie.NET MCP endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The path to serve from. Defaults to <see cref="McpPath"/>.</param>
    /// <returns>
    /// An <see cref="IEndpointConventionBuilder"/> for further endpoint configuration, such as
    /// <c>RequireAuthorization()</c>.
    /// </returns>
    /// <remarks>
    /// The endpoint reports pulse checker state, and with mutations turned on it can also run checks
    /// against your infrastructure. Require authorization on it in anything but a local development
    /// setup.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is <c>null</c>.</exception>
    public static IEndpointConventionBuilder MapHealthieMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = McpPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapMcp(pattern);
    }
}
