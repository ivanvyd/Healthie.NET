using Healthie.Abstractions.Diagnostics;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Insights;
using System.Diagnostics.Metrics;

namespace Healthie.DependencyInjection;

/// <summary>
/// Collects the library's own instruments in-process, so a board can show them.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="MeterListener"/> subscribed by meter name, which is how it reads instruments that are
/// <c>internal</c> to another assembly without that assembly exposing them. It is the same mechanism
/// OpenTelemetry uses, so this does not compete with an exporter: both can listen at once, and
/// neither sees the other.
/// </para>
/// <para>
/// Opt-in through <c>AddHealthieMetrics()</c>. A listener costs a callback on every recorded
/// measurement, which is small but not free, and an application exporting to an APM already has
/// somewhere better to look.
/// </para>
/// </remarks>
public sealed class MeterMetricsInsights : IMetricsInsights, IDisposable
{
    private readonly MeterListener _listener;
    private readonly object _gate = new();
    private readonly Dictionary<PulseCheckerHealth, long> _resultsByHealth = [];

    private long _checks;
    private long _transitions;
    private long _overlaps;
    private double _durationTotal;
    private long _durationCount;
    private double _durationMax;

    private readonly DateTime _since;

    /// <summary>Starts listening.</summary>
    /// <param name="timeProvider">Where the start time comes from.</param>
    public MeterMetricsInsights(TimeProvider? timeProvider = null)
    {
        _since = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == HealthieDiagnostics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _listener.SetMeasurementEventCallback<long>(OnLong);
        _listener.SetMeasurementEventCallback<double>(OnDouble);
        _listener.Start();
    }

    /// <inheritdoc />
    public MetricsSnapshot Snapshot(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return new MetricsSnapshot(
                _checks,
                new Dictionary<PulseCheckerHealth, long>(_resultsByHealth),
                _transitions,
                _overlaps,
                _durationCount == 0 ? null : TimeSpan.FromSeconds(_durationTotal / _durationCount),
                _durationCount == 0 ? null : TimeSpan.FromSeconds(_durationMax),
                _since);
        }
    }

    private void OnLong(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        // The health tag has to be read here: the span cannot outlive the callback, so it is resolved
        // before taking the lock rather than stashed.
        var health = HealthOf(tags);

        lock (_gate)
        {
            switch (instrument.Name)
            {
                case "healthie.check.results":
                    _checks += measurement;

                    if (health is { } reported)
                    {
                        _resultsByHealth[reported] = _resultsByHealth.GetValueOrDefault(reported) + measurement;
                    }

                    break;
                case "healthie.check.transitions":
                    _transitions += measurement;
                    break;
                case "healthie.check.overlaps":
                    _overlaps += measurement;
                    break;
            }
        }
    }

    private void OnDouble(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name != "healthie.check.duration")
        {
            return;
        }

        lock (_gate)
        {
            _durationTotal += measurement;
            _durationCount++;
            _durationMax = Math.Max(_durationMax, measurement);
        }
    }

    private static PulseCheckerHealth? HealthOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == HealthieDiagnostics.ResultTag
                && Enum.TryParse<PulseCheckerHealth>(tag.Value?.ToString(), ignoreCase: true, out var health))
            {
                return health;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();
}
