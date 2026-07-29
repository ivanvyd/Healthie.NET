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
    /// Add at least one sink -- <see cref="AddHealthieWebhookAlerts"/>,
    /// <see cref="AddHealthieSlackAlerts"/>, <see cref="AddHealthieMicrosoftTeamsAlerts"/>,
    /// <see cref="AddHealthiePagerDutyAlerts"/>, or your own
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

    /// <summary>
    /// Posts every alert to a Slack channel.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="webhookUrl">The incoming-webhook URL of the channel to post to.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// One more sink rather than a replacement, so this can sit alongside a webhook and a pager and
    /// each gets every alert. Configure the client by naming
    /// <see cref="SlackAlertSink.HttpClientName"/> in your own <c>AddHttpClient</c> call.
    /// </remarks>
    public static IServiceCollection AddHealthieSlackAlerts(this IServiceCollection services, Uri webhookUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(webhookUrl);

        services.AddHttpClient();

        return services.AddSingleton<IAlertSink>(provider =>
            new SlackAlertSink(provider.GetRequiredService<IHttpClientFactory>(), webhookUrl));
    }

    /// <summary>
    /// Posts every alert to a Microsoft Teams channel as an Adaptive Card.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="webhookUrl">The Workflows URL of the channel to post to.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// The URL is the one a Teams <em>Workflow</em> gives you, not an Office 365 connector -- those
    /// are retired, along with the payload they took. Configure the client by naming
    /// <see cref="MicrosoftTeamsAlertSink.HttpClientName"/> in your own <c>AddHttpClient</c> call.
    /// </remarks>
    public static IServiceCollection AddHealthieMicrosoftTeamsAlerts(this IServiceCollection services, Uri webhookUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(webhookUrl);

        services.AddHttpClient();

        return services.AddSingleton<IAlertSink>(provider =>
            new MicrosoftTeamsAlertSink(provider.GetRequiredService<IHttpClientFactory>(), webhookUrl));
    }

    /// <summary>
    /// Opens and closes PagerDuty incidents as checkers fail and recover.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="routingKey">The integration key of the PagerDuty service to alert.</param>
    /// <param name="endpoint">
    /// Where to send events. Defaults to <see cref="PagerDutyAlertSink.EventsEndpoint"/>; override it
    /// for the EU service region.
    /// </param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// Unlike the chat sinks, this one closes what it opened: a recovery resolves the incident the
    /// failure raised rather than posting a second message saying everything is fine.
    /// </remarks>
    public static IServiceCollection AddHealthiePagerDutyAlerts(
        this IServiceCollection services,
        string routingKey,
        Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        services.AddHttpClient();

        return services.AddSingleton<IAlertSink>(provider =>
            new PagerDutyAlertSink(provider.GetRequiredService<IHttpClientFactory>(), routingKey, endpoint));
    }
}
