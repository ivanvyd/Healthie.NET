using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Healthie.AI;

/// <summary>
/// Extension methods for registering Healthie.NET AI diagnostics with dependency injection.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Registers <see cref="IPulseDiagnostician"/>, which explains what a checker's recent history
    /// shows.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Requires an <c>IChatClient</c> in the container. Register one from whichever provider you
    /// use, for example:
    /// </para>
    /// <code>
    /// builder.Services.AddSingleton&lt;IChatClient&gt;(
    ///     new AnthropicClient().AsIChatClient("claude-opus-4-8"));
    ///
    /// builder.Services.AddHealthieAI();
    /// </code>
    /// <para>
    /// Diagnosing a checker sends its check result messages to that provider. See
    /// <see cref="PulseDiagnostician"/> for what is sent.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddHealthieAI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPulseDiagnostician, PulseDiagnostician>();

        return services;
    }
}
