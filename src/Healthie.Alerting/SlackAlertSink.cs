using Healthie.Abstractions.Enums;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Healthie.Alerting;

/// <summary>
/// Posts each alert to a Slack incoming webhook.
/// </summary>
/// <remarks>
/// A Slack webhook does not accept arbitrary JSON: it wants <c>text</c>, and optionally attachments
/// or blocks. Posting the generic payload at one produces <c>invalid_payload</c>, which is why
/// reaching Slack used to need something in between to reshape it.
/// </remarks>
public sealed class SlackAlertSink : IAlertSink
{
    /// <summary>The name this sink resolves its <see cref="HttpClient"/> under.</summary>
    public const string HttpClientName = "Healthie.Alerting.Slack";

    private readonly IHttpClientFactory _clients;
    private readonly Uri _webhookUrl;

    /// <summary>Initializes a new instance of the <see cref="SlackAlertSink"/> class.</summary>
    /// <param name="clients">The factory the request's client comes from.</param>
    /// <param name="webhookUrl">The incoming-webhook URL of the channel to post to.</param>
    public SlackAlertSink(IHttpClientFactory clients, Uri webhookUrl)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _webhookUrl = webhookUrl ?? throw new ArgumentNullException(nameof(webhookUrl));
    }

    /// <inheritdoc />
    public async Task SendAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var client = _clients.CreateClient(HttpClientName);

        using var response = await client
            .PostAsJsonAsync(_webhookUrl, SlackMessage.From(alert), cancellationToken)
            .ConfigureAwait(false);

        // Throwing is how a sink reports a failed delivery; the dispatcher logs it and carries on.
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// The JSON a <see cref="SlackAlertSink"/> posts.
/// </summary>
/// <param name="Text">The notification line, which is what a push notification shows.</param>
/// <param name="Attachments">The coloured detail block.</param>
public sealed record SlackMessage(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("attachments")] IReadOnlyList<SlackAttachment> Attachments)
{
    /// <summary>Builds the message for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    public static SlackMessage From(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var headline = alert.IsRecovery
            ? $"{alert.DisplayName} recovered"
            : $"{alert.DisplayName} is {alert.CurrentHealth}";

        var fields = new List<SlackField>
        {
            new("Status", alert.CurrentHealth.ToString(), Short: true),
            new("Was", alert.PreviousHealth?.ToString() ?? "never run", Short: true),
        };

        if (alert.Group is { } group)
        {
            fields.Add(new SlackField("Group", group, Short: true));
        }

        if (alert.Tags.Count > 0)
        {
            fields.Add(new SlackField("Tags", string.Join(", ", alert.Tags), Short: true));
        }

        if (!string.IsNullOrWhiteSpace(alert.Message))
        {
            fields.Add(new SlackField("Message", alert.Message, Short: false));
        }

        return new SlackMessage(
            headline,
            [new SlackAttachment(ColourOf(alert.CurrentHealth), alert.CheckerName, fields, ToUnixSeconds(alert.OccurredAt))]);
    }

    /// <summary>Slack's own status colours, which it renders as the bar down the attachment.</summary>
    private static string ColourOf(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Unhealthy => "danger",
        PulseCheckerHealth.Suspicious => "warning",
        PulseCheckerHealth.Healthy => "good",
        _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Unknown health."),
    };

    /// <summary>
    /// Slack timestamps events in Unix seconds.
    /// </summary>
    /// <remarks>
    /// Read as UTC rather than local: an alert's time is recorded in UTC, and a DateTime carrying
    /// Unspecified would otherwise be shifted by the server's offset on the way out.
    /// </remarks>
    private static long ToUnixSeconds(DateTime occurredAt) =>
        new DateTimeOffset(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
}

/// <summary>
/// One coloured block under a Slack message.
/// </summary>
/// <param name="Color">Slack's <c>good</c>, <c>warning</c> or <c>danger</c>.</param>
/// <param name="Footer">Shown small under the fields; the checker's full name goes here.</param>
/// <param name="Fields">The labelled values.</param>
/// <param name="Timestamp">When it happened, in Unix seconds.</param>
public sealed record SlackAttachment(
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("footer")] string Footer,
    [property: JsonPropertyName("fields")] IReadOnlyList<SlackField> Fields,
    [property: JsonPropertyName("ts")] long Timestamp);

/// <summary>
/// One labelled value in a Slack attachment.
/// </summary>
/// <param name="Title">The label.</param>
/// <param name="Value">The value.</param>
/// <param name="Short">Whether Slack may put it beside another rather than on its own line.</param>
public sealed record SlackField(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("short")] bool Short);
