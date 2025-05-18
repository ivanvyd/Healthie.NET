using System.ComponentModel;

namespace Healthie.Abstractions.Enums;

/// <summary>
/// Defines the available execution intervals for pulse checkers.
/// </summary>
public enum PulseInterval
{
    [Description("Every 1 second")]
    EverySecond = 1,
    [Description("Every 2 seconds")]
    Every2Seconds,
    [Description("Every 3 seconds")]
    Every3Seconds,
    [Description("Every 5 seconds")]
    Every5Seconds,
    [Description("Every 10 seconds")]
    Every10Seconds,
    [Description("Every 15 seconds")]
    Every15Seconds,
    [Description("Every 20 seconds")]
    Every20Seconds,
    [Description("Every 30 seconds")]
    Every30Seconds,
    [Description("Every 1 minute")]
    EveryMinute,
    [Description("Every 2 minutes")]
    Every2Minutes,
    [Description("Every 3 minutes")]
    Every3Minutes,
    [Description("Every 4 minutes")]
    Every4Minutes,
    [Description("Every 5 minutes")]
    Every5Minutes,
}
