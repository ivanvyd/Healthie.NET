using System.Net.Http.Json;

namespace Healthie.Alerting;

/// <summary>
/// Posts each alert as JSON to a URL.
/// </summary>
/// <remarks>
/// The one sink worth shipping, because it is the one that reaches everything else. Slack, Teams
/// through a Power Automate flow, Discord, PagerDuty's Events API, an internal service and a
/// no-code automation tool all accept an HTTP POST, so a webhook covers them with a payload shape
/// documented once rather than a package each.
/// </remarks>
public sealed class WebhookAlertSink : IAlertSink
{
    /// <summary>The name this sink resolves its <see cref="HttpClient"/> under.</summary>
    public const string HttpClientName = "Healthie.Alerting.Webhook";

    private readonly IHttpClientFactory _clients;
    private readonly Uri _url;

    /// <summary>Initializes a new instance of the <see cref="WebhookAlertSink"/> class.</summary>
    /// <param name="clients">The factory the request's client comes from.</param>
    /// <param name="url">Where to post.</param>
    public WebhookAlertSink(IHttpClientFactory clients, Uri url)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    /// <inheritdoc />
    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var client = _clients.CreateClient(HttpClientName);

        using var response = await client
            .PostAsJsonAsync(_url, WebhookPayload.From(alert), cancellationToken)
            .ConfigureAwait(false);

        // Throwing is how a sink reports a failed delivery; the dispatcher logs it and carries on.
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// The JSON a <see cref="WebhookAlertSink"/> posts.
/// </summary>
/// <remarks>
/// A shape of its own rather than serializing <see cref="Alert"/> directly. The payload is a
/// contract with whatever is on the other end, and letting it track an internal record means a
/// refactor renames a field in somebody's automation.
/// </remarks>
/// <param name="Checker">The checker's name.</param>
/// <param name="DisplayName">The checker's display name.</param>
/// <param name="Group">The checker's group, or <c>null</c>.</param>
/// <param name="Tags">The checker's tags.</param>
/// <param name="Status">The health now.</param>
/// <param name="PreviousStatus">The health being left, or <c>null</c> if the checker had never run.</param>
/// <param name="IsRecovery">Whether this says the checker came back.</param>
/// <param name="Message">The check's own message.</param>
/// <param name="OccurredAt">When the change was observed, in UTC.</param>
/// <param name="DeduplicationKey">Identifies the ongoing incident rather than this occurrence.</param>
public sealed record WebhookPayload(
    string Checker,
    string DisplayName,
    string? Group,
    IReadOnlyList<string> Tags,
    string Status,
    string? PreviousStatus,
    bool IsRecovery,
    string Message,
    DateTime OccurredAt,
    string DeduplicationKey)
{
    /// <summary>Builds the payload for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    public static WebhookPayload From(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return new WebhookPayload(
            alert.CheckerName,
            alert.DisplayName,
            alert.Group,
            alert.Tags,
            alert.CurrentHealth.ToString(),
            alert.PreviousHealth?.ToString(),
            alert.IsRecovery,
            alert.Message,
            alert.OccurredAt,
            alert.DeduplicationKey);
    }
}
