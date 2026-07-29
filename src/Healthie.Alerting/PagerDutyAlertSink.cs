using Healthie.Abstractions.Enums;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Healthie.Alerting;

/// <summary>
/// Opens and closes PagerDuty incidents through the Events API v2.
/// </summary>
/// <remarks>
/// <para>
/// The one destination a generic webhook cannot reach usefully. PagerDuty does not want a health
/// change posted at it; it wants to be told an incident has started or ended, and which incident,
/// so that a checker flapping between suspicious and unhealthy pages somebody once rather than
/// every time it moves.
/// </para>
/// <para>
/// <see cref="Alert.DeduplicationKey"/> and <see cref="Alert.IsRecovery"/> already existed for
/// exactly this: the key becomes <c>dedup_key</c>, and a recovery becomes <c>resolve</c> rather
/// than another <c>trigger</c>.
/// </para>
/// </remarks>
public sealed class PagerDutyAlertSink : IAlertSink
{
    /// <summary>The name this sink resolves its <see cref="HttpClient"/> under.</summary>
    public const string HttpClientName = "Healthie.Alerting.PagerDuty";

    /// <summary>Where the Events API takes events.</summary>
    public static readonly Uri EventsEndpoint = new("https://events.pagerduty.com/v2/enqueue");

    private readonly IHttpClientFactory _clients;
    private readonly string _routingKey;
    private readonly Uri _endpoint;

    /// <summary>Initializes a new instance of the <see cref="PagerDutyAlertSink"/> class.</summary>
    /// <param name="clients">The factory the request's client comes from.</param>
    /// <param name="routingKey">The integration key of the PagerDuty service to alert.</param>
    /// <param name="endpoint">Where to send events. Defaults to <see cref="EventsEndpoint"/>.</param>
    public PagerDutyAlertSink(IHttpClientFactory clients, string routingKey, Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _routingKey = routingKey;
        _endpoint = endpoint ?? EventsEndpoint;
    }

    /// <inheritdoc />
    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var client = _clients.CreateClient(HttpClientName);

        using var response = await client
            .PostAsJsonAsync(_endpoint, PagerDutyEvent.From(alert, _routingKey), cancellationToken)
            .ConfigureAwait(false);

        // Throwing is how a sink reports a failed delivery; the dispatcher logs it and carries on.
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// One PagerDuty Events API v2 event.
/// </summary>
/// <param name="RoutingKey">The integration key of the service to alert.</param>
/// <param name="EventAction">Either <c>trigger</c> or <c>resolve</c>.</param>
/// <param name="DedupKey">Identifies the incident, so repeats update it instead of opening another.</param>
/// <param name="Payload">What the incident says. Omitted on a resolve, which PagerDuty allows.</param>
public sealed record PagerDutyEvent(
    [property: JsonPropertyName("routing_key")] string RoutingKey,
    [property: JsonPropertyName("event_action")] string EventAction,
    [property: JsonPropertyName("dedup_key")] string DedupKey,
    [property: JsonPropertyName("payload"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PagerDutyEventPayload? Payload)
{
    /// <summary>Builds the event for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    /// <param name="routingKey">The integration key of the service to alert.</param>
    public static PagerDutyEvent From(Alert alert, string routingKey)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // A recovery closes the incident the failure opened, which is the whole reason the
        // deduplication key excludes the health and the time.
        return alert.IsRecovery
            ? new PagerDutyEvent(routingKey, "resolve", alert.DeduplicationKey, Payload: null)
            : new PagerDutyEvent(
                routingKey,
                "trigger",
                alert.DeduplicationKey,
                PagerDutyEventPayload.From(alert));
    }
}

/// <summary>
/// What a triggered PagerDuty incident says.
/// </summary>
/// <param name="Summary">The one line shown in the incident list and the page.</param>
/// <param name="Severity">PagerDuty's own scale: <c>critical</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="Source">Where the problem is, which PagerDuty groups and searches by.</param>
/// <param name="Component">The component within that source.</param>
/// <param name="Group">The checker's group, or <c>null</c>.</param>
/// <param name="CustomDetails">Everything else, shown on the incident.</param>
public sealed record PagerDutyEventPayload(
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("component")] string Component,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("custom_details")] IReadOnlyDictionary<string, string> CustomDetails)
{
    /// <summary>Builds the payload for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    public static PagerDutyEventPayload From(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return new PagerDutyEventPayload(
            $"{alert.DisplayName} is {alert.CurrentHealth}",
            SeverityOf(alert.CurrentHealth),
            alert.CheckerName,
            alert.DisplayName,
            alert.Group,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message"] = alert.Message,
                ["previous_health"] = alert.PreviousHealth?.ToString() ?? "none",
                ["tags"] = string.Join(", ", alert.Tags),
                ["observed_at_utc"] = alert.OccurredAt.ToString("O"),
            });
    }

    /// <summary>Maps a health onto PagerDuty's severity scale.</summary>
    /// <remarks>
    /// Suspicious is a warning rather than critical on purpose: it is the state that says "failing,
    /// but not past the threshold yet", and paging on it would defeat having a threshold.
    /// </remarks>
    private static string SeverityOf(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Unhealthy => "critical",
        PulseCheckerHealth.Suspicious => "warning",
        PulseCheckerHealth.Healthy => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Unknown health."),
    };
}
