using Healthie.Api.Controllers;
using Healthie.Api.Conventions;
using Healthie.Api.Routes;
using Microsoft.Extensions.DependencyInjection;

namespace Healthie.Api;

public static class StartupExtensions
{
    /// <summary>
    /// Adds and configures the HealthCheckersController.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="baseRoute">The base route for the HealthCheckersController. Defaults to "healthie", which matches the controller's default RouteAttribute.</param>
    /// <param name="requireAuthorization">A flag to indicate if authorization is required for the controller. Defaults to false.</param>
    /// <param name="authorizationPolicy">The name of the authorization policy to apply if authorization is required. If null or empty and requireAuthorization is true, it requires an authenticated user.</param>
    /// <returns>An <see cref="IMvcBuilder"/> that can be used to further configure the MVC services.</returns>
    public static IMvcBuilder AddHealthieController(
        this IServiceCollection services,
        string baseRoute = RoutesConstants.HealthieApiRoute,
        bool requireAuthorization = false,
        string? authorizationPolicy = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrWhiteSpace(baseRoute)) throw new ArgumentException("Base route cannot be empty.", nameof(baseRoute));

        var mvcBuilder = services.AddControllers(options =>
        {
            options.Conventions.Add(new HealthCheckersControllerConvention(baseRoute, requireAuthorization, authorizationPolicy));
        });

        // Ensure HealthCheckersController from Healthie.Api assembly is discovered.
        // This is crucial if this extension method is called from an application in a different assembly.
        mvcBuilder.AddApplicationPart(typeof(HealthCheckersController).Assembly);

        return mvcBuilder;
    }
}
