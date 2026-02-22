using Healthie.Api.Controllers;
using Healthie.Api.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Api;

/// <summary>
/// Extension methods for registering the Healthie API controller and its conventions
/// with the ASP.NET Core dependency injection container.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Adds and configures the HealthCheckersController at the <c>/healthie</c> route prefix.
    /// API endpoints are served under <c>/healthie/*</c> (e.g. <c>/healthie/intervals</c>,
    /// <c>/healthie/{checkerName}/trigger</c>).
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="requireAuthorization">Whether to require authorization on the controller. Defaults to <c>false</c>.</param>
    /// <param name="authorizationPolicy">
    /// The name of the authorization policy to apply when <paramref name="requireAuthorization"/> is <c>true</c>.
    /// If <c>null</c>, a default policy requiring an authenticated user is applied.
    /// </param>
    /// <returns>An <see cref="IMvcBuilder"/> that can be used to further configure the MVC services.</returns>
    public static IMvcBuilder AddHealthieController(
        this IServiceCollection services,
        bool requireAuthorization = false,
        string? authorizationPolicy = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        var mvcBuilder = services.AddControllers(options =>
        {
            options.Conventions.Add(new HealthCheckersControllerConvention(requireAuthorization, authorizationPolicy));
        });

        // Ensure HealthCheckersController from Healthie.Api assembly is discovered.
        mvcBuilder.AddApplicationPart(typeof(HealthCheckersController).Assembly);

        return mvcBuilder;
    }
}
