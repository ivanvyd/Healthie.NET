using Healthie.Abstractions.Models;

namespace Healthie.Abstractions.Extensions;

public static class PulseIntervalExtensions
{
    public static string ToCronExpression(this PulseInterval pulseInterval)
    {
        return pulseInterval switch
        {
            PulseInterval.EverySecond => "0/1 * * * * ?",
            PulseInterval.Every2Seconds => "0/2 * * * * ?",
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
}
