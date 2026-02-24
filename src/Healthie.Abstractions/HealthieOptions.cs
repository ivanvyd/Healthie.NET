namespace Healthie.Abstractions;

/// <summary>
/// Global configuration options for the Healthie.NET library.
/// </summary>
public class HealthieOptions
{
    private uint _maxHistoryLength = 10;

    /// <summary>
    /// Gets or sets the maximum number of trigger history entries to retain per pulse checker.
    /// </summary>
    /// <remarks>Valid range is 1-10. Defaults to 10. Values outside the range are clamped.</remarks>
    public uint MaxHistoryLength
    {
        get => _maxHistoryLength;
        set => _maxHistoryLength = Math.Clamp(value, 1, 10);
    }
}
