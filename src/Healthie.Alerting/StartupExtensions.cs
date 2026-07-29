using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Healthie.Alerting;

/// <summary>
/// Extension methods for registering alerting with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Turns health changes into alerts and delivers them to the registered sinks.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">Optionally adjusts which changes alert, and how hard to try.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Add at least one sink -- <see cref="AddHealthieWebhookAlerts"/>, or your own
    /// <see cref="IAlertSink"/>. With none, the dispatcher says so once at startup and does nothing,
    /// rather than queueing alerts nobody will read.
    /// </remarks>
    public static IServiceCollection AddHealthieAlerts(
        this IServiceCollection services,
        Action<HealthieAlertOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new HealthieAlertOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.AddHostedService<AlertDispatcher>();

        return services;
    }

    /// <summary>
    /// Posts every alert as JSON to a URL.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="url">Where to post.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Registered as one more sink rather than replacing any, so several webhooks can be added and
    /// each gets every alert. Configure the client -- its timeout, its handler, an auth header -- by
    /// naming <see cref="WebhookAlertSink.HttpClientName"/> in your own <c>AddHttpClient</c> call.
    /// </remarks>
    public static IServiceCollection AddHealthieWebhookAlerts(this IServiceCollection services, Uri url)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(url);

        services.AddHttpClient();

        return services.AddSingleton<IAlertSink>(provider =>
            new WebhookAlertSink(provider.GetRequiredService<IHttpClientFactory>(), url));
    }
}
