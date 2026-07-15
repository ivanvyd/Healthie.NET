namespace Healthie.Abstractions;

/// <summary>
/// Global configuration options for the Healthie.NET library.
/// </summary>
public class HealthieOptions
{
    /// <summary>The largest value <see cref="MaxHistoryLength"/> accepts.</summary>
    /// <remarks>
    /// History is stored inline with each checker's state, so this bounds how much every write
    /// carries. The ceiling exists to keep that from growing without limit.
    /// </remarks>
    public const uint MaxSupportedHistoryLength = 100;

    private uint _maxHistoryLength = 10;

    /// <summary>
    /// Gets or sets the maximum number of trigger history entries to retain per pulse checker.
    /// </summary>
    /// <remarks>
    /// Valid range is 1 to <see cref="MaxSupportedHistoryLength"/>. Defaults to 10. Values outside
    /// the range are clamped. Longer history gives the dashboard more to plot, at the cost of a
    /// larger state document on every write.
    /// </remarks>
    public uint MaxHistoryLength
    {
        get => _maxHistoryLength;
        set => _maxHistoryLength = Math.Clamp(value, 1, MaxSupportedHistoryLength);
    }
}
