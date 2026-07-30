namespace Healthie.Abstractions.Insights;

/// <summary>
/// One place alerts are delivered to, and how it has been going.
/// </summary>
/// <param name="Name">The sink's type name, which is what identifies it on the board.</param>
/// <param name="Delivered">How many alerts it has accepted.</param>
/// <param name="Failed">How many it refused, timed out on, or threw over.</param>
/// <param name="LastError">The most recent failure's message, or <c>null</c> if it has never failed.</param>
/// <remarks>
/// Counted per sink rather than in total because that is the question being asked: with three sinks
/// configured, "one alert did not get through" is a very different morning from "Slack is down".
/// </remarks>
public sealed record AlertSinkStatus(string Name, int Delivered, int Failed, string? LastError)
{
    /// <summary>Whether this sink is currently getting alerts through.</summary>
    /// <remarks>
    /// Judged on the last attempt rather than the ratio: a sink that failed a hundred times and is
    /// working now is working, and one that has delivered thousands and just started failing is the
    /// thing worth showing in red.
    /// </remarks>
    public bool IsHealthy => LastError is null;
}
