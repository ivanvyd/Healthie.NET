using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Healthie.Abstractions.Enums;

/// <summary>
/// Defines the available execution intervals for pulse checkers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
[Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
public enum PulseInterval
{
    /// <summary>
    /// Execute every 1 second.
    /// </summary>
    [Description("Every 1 second")]
    EverySecond = 1,

    /// <summary>
    /// Execute every 2 seconds.
    /// </summary>
    [Description("Every 2 seconds")]
    Every2Seconds,

    /// <summary>
    /// Execute every 3 seconds.
    /// </summary>
    [Description("Every 3 seconds")]
    Every3Seconds,

    /// <summary>
    /// Execute every 5 seconds.
    /// </summary>
    [Description("Every 5 seconds")]
    Every5Seconds,

    /// <summary>
    /// Execute every 10 seconds.
    /// </summary>
    [Description("Every 10 seconds")]
    Every10Seconds,

    /// <summary>
    /// Execute every 15 seconds.
    /// </summary>
    [Description("Every 15 seconds")]
    Every15Seconds,

    /// <summary>
    /// Execute every 20 seconds.
    /// </summary>
    [Description("Every 20 seconds")]
    Every20Seconds,

    /// <summary>
    /// Execute every 30 seconds.
    /// </summary>
    [Description("Every 30 seconds")]
    Every30Seconds,

    /// <summary>
    /// Execute every 1 minute.
    /// </summary>
    [Description("Every 1 minute")]
    EveryMinute,

    /// <summary>
    /// Execute every 2 minutes.
    /// </summary>
    [Description("Every 2 minutes")]
    Every2Minutes,

    /// <summary>
    /// Execute every 3 minutes.
    /// </summary>
    [Description("Every 3 minutes")]
    Every3Minutes,

    /// <summary>
    /// Execute every 4 minutes.
    /// </summary>
    [Description("Every 4 minutes")]
    Every4Minutes,

    /// <summary>
    /// Execute every 5 minutes.
    /// </summary>
    [Description("Every 5 minutes")]
    Every5Minutes,
}
