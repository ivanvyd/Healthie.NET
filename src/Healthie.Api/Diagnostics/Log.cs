using Microsoft.Extensions.Logging;

namespace Healthie.Api.Diagnostics;

/// <summary>
/// The log messages <see cref="Healthie.Api"/> writes.
/// </summary>
/// <remarks>
/// Source-generated, as in the other packages, and with event ids in their own 4000 range so a
/// filter written against them keeps meaning what it meant.
/// </remarks>
internal static partial class Log
{
    /// <remarks>
    /// Warning, and only once at startup. It describes a configuration an operator chose and can
    /// change, not something going wrong at runtime, so repeating it per request would bury the
    /// logs it is trying to be noticed in.
    /// </remarks>
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Warning,
        Message = "Healthie: {Count} endpoint(s) that can change a pulse checker are reachable " +
            "without authenticating -- {Routes}. Anyone who can reach this application can stop a " +
            "checker or clear a failing streak, which hides an incident rather than reporting one. " +
            "Pass requireAuthorization: true to AddHealthieController, or apply your own " +
            "authorization to these endpoints.")]
    public static partial void MutatingEndpointsAreUnauthenticated(ILogger logger, int count, string routes);
}
