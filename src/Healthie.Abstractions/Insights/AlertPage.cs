namespace Healthie.Abstractions.Insights;

/// <summary>
/// One page of the alert history, newest first.
/// </summary>
/// <param name="Alerts">The alerts on this page.</param>
/// <param name="Total">How many are held in total, which is what the pager counts against.</param>
/// <param name="StoredIn">
/// The name of the state provider the history is kept in, so the board can say where it went and
/// whether it will still be there after a restart.
/// </param>
/// <param name="Capacity">
/// How many alerts are kept before the oldest is discarded. The history is bounded on purpose: it is
/// a window onto what happened, and the record of record is wherever the sinks deliver to.
/// </param>
public sealed record AlertPage(
    IReadOnlyList<AlertInsight> Alerts,
    int Total,
    string StoredIn,
    int Capacity);
