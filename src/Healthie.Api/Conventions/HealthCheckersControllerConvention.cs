using Healthie.Api.Controllers;
using Healthie.Api.Routes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Healthie.Api.Conventions;

/// <summary>
/// An <see cref="IApplicationModelConvention"/> that configures the route and authorization
/// for the <see cref="HealthCheckersController"/>.
/// </summary>
internal class HealthCheckersControllerConvention : IApplicationModelConvention
{
    private readonly string _baseRoute;
    private readonly bool _requireAuthorization;
    private readonly string? _authorizationPolicyName;

    public HealthCheckersControllerConvention(string baseRoute, bool requireAuthorization, string? authorizationPolicyName)
    {
        _baseRoute = baseRoute.TrimEnd('/');
        _requireAuthorization = requireAuthorization;
        _authorizationPolicyName = authorizationPolicyName;
    }

    public void Apply(ApplicationModel application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));

        foreach (var controller in application.Controllers)
        {
            if (controller.ControllerType == typeof(HealthCheckersController))
            {
                // Apply custom base route
                // The HealthCheckersController is defined with [Route("healthie")].
                // This convention updates that route to the provided _baseRoute.
                // Action routes are relative to the controller's route, so they will
                // automatically be prefixed with the new _baseRoute.
                if (!string.Equals(_baseRoute, RoutesConstants.HealthieApiRoute, StringComparison.OrdinalIgnoreCase))
                {
                    // Find the controller's selector that comes from its [Route("healthie")] attribute.
                    // There should typically be one such selector.
                    foreach (var selector in controller.Selectors)
                    {
                        if (selector.AttributeRouteModel != null &&
                            RoutesConstants.HealthieApiRoute.Equals(selector.AttributeRouteModel.Template, StringComparison.OrdinalIgnoreCase))
                        {
                            // Replace the template of the controller's route.
                            selector.AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(_baseRoute));
                        }
                    }
                }

                // Apply authorization policy
                if (_requireAuthorization)
                {
                    if (!string.IsNullOrEmpty(_authorizationPolicyName))
                    {
                        controller.Filters.Add(new AuthorizeFilter(_authorizationPolicyName));
                    }
                    else
                    {
                        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                        controller.Filters.Add(new AuthorizeFilter(policy));
                    }
                }
            }
        }
    }
}
