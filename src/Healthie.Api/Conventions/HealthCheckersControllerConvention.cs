using Healthie.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Healthie.Api.Conventions;

/// <summary>
/// An <see cref="IApplicationModelConvention"/> that configures authorization
/// for the <see cref="HealthCheckersController"/>.
/// </summary>
/// <param name="requireAuthorization">Whether to require authorization on the controller.</param>
/// <param name="authorizationPolicyName">
/// The name of the authorization policy to apply. If <c>null</c> and
/// <paramref name="requireAuthorization"/> is <c>true</c>, a default policy
/// requiring an authenticated user is used.
/// </param>
internal class HealthCheckersControllerConvention(
    bool requireAuthorization,
    string? authorizationPolicyName) : IApplicationModelConvention
{
    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!requireAuthorization) return;

        foreach (var controller in application.Controllers)
        {
            if (controller.ControllerType == typeof(HealthCheckersController))
            {
                if (!string.IsNullOrEmpty(authorizationPolicyName))
                {
                    controller.Filters.Add(new AuthorizeFilter(authorizationPolicyName));
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
