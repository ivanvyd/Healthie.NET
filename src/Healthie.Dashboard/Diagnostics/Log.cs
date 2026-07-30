using Microsoft.Extensions.Logging;

namespace Healthie.Dashboard.Diagnostics;

/// <summary>
/// The log messages the dashboard writes.
/// </summary>
/// <remarks>
/// Source-generated, as in the other packages, and with event ids in their own 5000 range so a
/// filter written against them keeps meaning what it meant.
/// </remarks>
internal static partial class Log
{
    /// <remarks>
    /// Warning, and only once at startup. It describes a configuration an operator chose and can
    /// change, not something going wrong at runtime.
    /// </remarks>
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Warning,
        Message = "Healthie: the dashboard at {Path} is reachable without authenticating and its " +
            "controls are on, so anyone who can reach this application can pause a checker or reset " +
            "a failing streak. Chain RequireAuthorization() onto MapHealthieUI(), or set " +
            "HealthieUIOptions.AllowMutations to false to serve it read-only.")]
    public static partial void DashboardIsUnauthenticatedAndWritable(ILogger logger, string path);
}
