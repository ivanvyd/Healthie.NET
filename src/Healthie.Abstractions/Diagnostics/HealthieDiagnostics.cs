using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Healthie.Abstractions.Diagnostics;

/// <summary>
/// The metrics and traces this library emits about itself.
/// </summary>
/// <remarks>
/// <para>
/// A monitoring library that cannot be monitored is a gap, and closing it costs nothing here:
/// <see cref="System.Diagnostics.Metrics.Meter"/> and <see cref="System.Diagnostics.ActivitySource"/>
/// are framework types, so <c>Healthie.Abstractions</c> keeps its single dependency. There is no
/// OpenTelemetry package to install and nothing to register -- OpenTelemetry finds both by name:
/// </para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(m => m.AddMeter(HealthieDiagnostics.MeterName))
///     .WithTracing(t => t.AddSource(HealthieDiagnostics.ActivitySourceName));
/// </code>
/// <para>
/// Only low-cardinality values are used as tags. A checker's name and group are bounded by how many
/// checkers an application registers; its tags are not, are editable from the dashboard, and would
/// multiply every series by their number, so they are deliberately absent.
/// </para>
/// </remarks>
public static class HealthieDiagnostics
{
    /// <summary>The meter name to pass to <c>AddMeter</c>.</summary>
    public const string MeterName = "Healthie.NET";

    /// <summary>The activity source name to pass to <c>AddSource</c>.</summary>
    public const string ActivitySourceName = "Healthie.NET";

    /// <summary>Tag carrying <see cref="IPulseChecker.Name"/>.</summary>
    public const string CheckerNameTag = "healthie.checker.name";

    /// <summary>Tag carrying the checker's group, absent when it has none.</summary>
    public const string CheckerGroupTag = "healthie.checker.group";

    /// <summary>Tag carrying the health a check reported.</summary>
    public const string ResultTag = "healthie.check.result";

    private static readonly string Version =
        typeof(HealthieDiagnostics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HealthieDiagnostics).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private static readonly Meter Meter = new(MeterName, Version);

    /// <summary>
    /// How long a check took, in seconds.
    /// </summary>
    /// <remarks>
    /// Seconds rather than milliseconds, and a plural name, because that is what OpenTelemetry's
    /// conventions ask of a duration histogram. It measures the user's check alone -- reading and
    /// writing state are this library's own work and would flatter or distort the component's
    /// apparent latency.
    /// </remarks>
    internal static readonly Histogram<double> CheckDuration = Meter.CreateHistogram<double>(
        "healthie.check.duration",
        unit: "s",
        description: "Duration of a pulse check.");

    /// <summary>Number of checks that completed, tagged with the health each reported.</summary>
    internal static readonly Counter<long> CheckResults = Meter.CreateCounter<long>(
        "healthie.check.results",
        unit: "{check}",
        description: "Pulse checks completed, by the health they reported.");

    /// <summary>
    /// Number of times a check produced a state different from the stored one.
    /// </summary>
    /// <remarks>
    /// This is the one worth alerting on. The result counter climbs on every tick; this moves only
    /// when something actually changed.
    /// </remarks>
    internal static readonly Counter<long> StateTransitions = Meter.CreateCounter<long>(
        "healthie.check.transitions",
        unit: "{transition}",
        description: "Pulse checker state changes.");

    /// <summary>
    /// Number of triggers that returned immediately because the previous one had not finished.
    /// </summary>
    /// <remarks>
    /// A checker whose check takes longer than its interval shows up here and nowhere else -- it
    /// looks healthy, and is quietly running at a fraction of the rate it was asked to.
    /// </remarks>
    internal static readonly Counter<long> OverlappedTriggers = Meter.CreateCounter<long>(
        "healthie.check.overlaps",
        unit: "{trigger}",
        description: "Triggers skipped because the previous check was still running.");
}
