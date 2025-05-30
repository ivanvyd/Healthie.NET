using System.ComponentModel;

namespace Healthie.Abstractions.Enums;

/// <summary>
/// Defines the possible health states of a pulse checker result.
/// </summary>
public enum PulseCheckerHealth
{
    /// <summary>
    /// The pulse checker is healthy, indicating everything is working correctly.
    /// </summary>
    [Description("Healthy")]
    Healthy = 0,
    
    /// <summary>
    /// The pulse checker is in a suspicious state, indicating potential issues that require attention.
    /// </summary>
    [Description("Suspicious")]
    Suspicious = 1,
    
    /// <summary>
    /// The pulse checker is unhealthy, indicating a failure or error condition.
    /// </summary>
    [Description("Unhealthy")]
    Unhealthy = 2
}