using Healthie.Abstractions.Enums;

namespace Healthie.Abstractions.Extensions;

/// <summary>
/// Provides extension methods for the <see cref="PulseInterval"/> enum.
/// </summary>
public static class PulseIntervalExtensions
{
    /// <summary>
    /// Converts a <see cref="PulseInterval"/> enum value to its corresponding CRON expression string.
    /// </summary>
    /// <param name="pulseInterval">The pulse interval to convert.</param>
    /// <returns>A CRON expression string representing the interval.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the <paramref name="pulseInterval"/> is not a defined enum value.</exception>
    public static string ToCronExpression(this PulseInterval pulseInterval)
    {
        return pulseInterval switch
        {
            PulseInterval.EverySecond => "0/1 * * * * ?",
            PulseInterval.Every2Seconds => "0/2 * * * * ?",
            PulseInterval.Every3Seconds => "0/3 * * * * ?",
            PulseInterval.Every5Seconds => "0/5 * * * * ?",
            PulseInterval.Every10Seconds => "0/10 * * * * ?",
            PulseInterval.Every15Seconds => "0/15 * * * * ?",
            PulseInterval.Every20Seconds => "0/20 * * * * ?",
            PulseInterval.Every30Seconds => "0/30 * * * * ?",
            PulseInterval.EveryMinute => "0 0/1 * 1/1 * ?",
            PulseInterval.Every2Minutes => "0 0/2 * 1/1 * ?",
            PulseInterval.Every3Minutes => "0 0/3 * 1/1 * ?",
            PulseInterval.Every4Minutes => "0 0/4 * 1/1 * ?",
            PulseInterval.Every5Minutes => "0 0/5 * 1/1 * ?",
            _ => throw new ArgumentOutOfRangeException(nameof(pulseInterval), $"Not supported interval: {pulseInterval}")
        };
    }

    /// <summary>
    /// Converts a <see cref="PulseInterval"/> to its equivalent <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="pulseInterval">The pulse interval to convert.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the interval duration.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pulseInterval"/> is not a defined enum value.
    /// </exception>
    public static TimeSpan ToTimeSpan(this PulseInterval pulseInterval)
    {
        return pulseInterval switch
        {
            PulseInterval.EverySecond => TimeSpan.FromSeconds(1),
            PulseInterval.Every2Seconds => TimeSpan.FromSeconds(2),
            PulseInterval.Every3Seconds => TimeSpan.FromSeconds(3),
            PulseInterval.Every5Seconds => TimeSpan.FromSeconds(5),
            PulseInterval.Every10Seconds => TimeSpan.FromSeconds(10),
            PulseInterval.Every15Seconds => TimeSpan.FromSeconds(15),
            PulseInterval.Every20Seconds => TimeSpan.FromSeconds(20),
            PulseInterval.Every30Seconds => TimeSpan.FromSeconds(30),
            PulseInterval.EveryMinute => TimeSpan.FromMinutes(1),
            PulseInterval.Every2Minutes => TimeSpan.FromMinutes(2),
            PulseInterval.Every3Minutes => TimeSpan.FromMinutes(3),
            PulseInterval.Every4Minutes => TimeSpan.FromMinutes(4),
            PulseInterval.Every5Minutes => TimeSpan.FromMinutes(5),
            _ => throw new ArgumentOutOfRangeException(
                nameof(pulseInterval),
                $"Not supported interval: {pulseInterval}")
        };
    }
}
