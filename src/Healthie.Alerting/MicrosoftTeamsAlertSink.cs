using Healthie.Abstractions.Enums;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Healthie.Alerting;

/// <summary>
/// Posts each alert to a Microsoft Teams channel as an Adaptive Card.
/// </summary>
/// <remarks>
/// <para>
/// Targets the Workflows URL, not the old Office 365 connector. Microsoft has retired those
/// connectors and the <c>MessageCard</c> payload they took, so a sink written to that shape would
/// have arrived already dead. A Workflows webhook takes the same envelope the Bot Framework uses:
/// a message whose attachment is an Adaptive Card.
/// </para>
/// <para>
/// This is what the webhook sink meant by "Teams through a Power Automate flow" -- the flow existed
/// to reshape the payload, and shaping it correctly here removes the flow.
/// </para>
/// </remarks>
public sealed class MicrosoftTeamsAlertSink : IAlertSink
{
    /// <summary>The name this sink resolves its <see cref="HttpClient"/> under.</summary>
    public const string HttpClientName = "Healthie.Alerting.MicrosoftTeams";

    private readonly IHttpClientFactory _clients;
    private readonly Uri _webhookUrl;

    /// <summary>Initializes a new instance of the <see cref="MicrosoftTeamsAlertSink"/> class.</summary>
    /// <param name="clients">The factory the request's client comes from.</param>
    /// <param name="webhookUrl">The Workflows URL of the channel to post to.</param>
    public MicrosoftTeamsAlertSink(IHttpClientFactory clients, Uri webhookUrl)
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
            .PostAsJsonAsync(_webhookUrl, TeamsMessage.From(alert), cancellationToken)
            .ConfigureAwait(false);

        // Throwing is how a sink reports a failed delivery; the dispatcher logs it and carries on.
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// The envelope a Teams Workflows webhook takes.
/// </summary>
/// <param name="Type">Always <c>message</c>.</param>
/// <param name="Attachments">Exactly one, holding the card.</param>
public sealed record TeamsMessage(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("attachments")] IReadOnlyList<TeamsAttachment> Attachments)
{
    /// <summary>Builds the message for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    public static TeamsMessage From(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return new TeamsMessage(
            "message",
            [new TeamsAttachment(
                "application/vnd.microsoft.card.adaptive",
                ContentUrl: null,
                AdaptiveCard.From(alert))]);
    }
}

/// <summary>
/// One attachment on a Teams message.
/// </summary>
/// <param name="ContentType">Always the Adaptive Card content type.</param>
/// <param name="ContentUrl">Always <c>null</c>; the card is inline.</param>
/// <param name="Content">The card.</param>
public sealed record TeamsAttachment(
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("contentUrl")] string? ContentUrl,
    [property: JsonPropertyName("content")] AdaptiveCard Content);

/// <summary>
/// The Adaptive Card a Teams channel renders.
/// </summary>
/// <param name="Type">Always <c>AdaptiveCard</c>.</param>
/// <param name="Schema">The Adaptive Card schema URL, which Teams requires.</param>
/// <param name="Version">The card schema version.</param>
/// <param name="Body">The card's elements.</param>
public sealed record AdaptiveCard(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("$schema")] string Schema,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("body")] IReadOnlyList<object> Body)
{
    /// <summary>Builds the card for an alert.</summary>
    /// <param name="alert">The alert to describe.</param>
    public static AdaptiveCard From(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var headline = alert.IsRecovery
            ? $"{alert.DisplayName} recovered"
            : $"{alert.DisplayName} is {alert.CurrentHealth}";

        var facts = new List<AdaptiveFact>
        {
            new("Checker", alert.CheckerName),
            new("Status", alert.CurrentHealth.ToString()),
            new("Was", alert.PreviousHealth?.ToString() ?? "never run"),
            new("Observed", $"{alert.OccurredAt:yyyy-MM-dd HH:mm:ss} UTC"),
        };

        if (alert.Group is { } group)
        {
            facts.Add(new AdaptiveFact("Group", group));
        }

        if (alert.Tags.Count > 0)
        {
            facts.Add(new AdaptiveFact("Tags", string.Join(", ", alert.Tags)));
        }

        var body = new List<object>
        {
            new AdaptiveTextBlock(headline, Weight: "Bolder", Size: "Medium", Color: ColourOf(alert.CurrentHealth), Wrap: true),
            new AdaptiveFactSet(facts),
        };

        if (!string.IsNullOrWhiteSpace(alert.Message))
        {
            body.Add(new AdaptiveTextBlock(alert.Message, Weight: null, Size: null, Color: null, Wrap: true));
        }

        return new AdaptiveCard("AdaptiveCard", "http://adaptivecards.io/schemas/adaptive-card.json", "1.4", body);
    }

    /// <summary>Adaptive Cards name their colours rather than taking hex.</summary>
    private static string ColourOf(PulseCheckerHealth health) => health switch
    {
        PulseCheckerHealth.Unhealthy => "Attention",
        PulseCheckerHealth.Suspicious => "Warning",
        PulseCheckerHealth.Healthy => "Good",
        _ => throw new ArgumentOutOfRangeException(nameof(health), health, "Unknown health."),
    };
}

/// <summary>
/// A line of text on an Adaptive Card.
/// </summary>
/// <param name="Text">The text.</param>
/// <param name="Weight">How bold, or <c>null</c> for the default.</param>
/// <param name="Size">How large, or <c>null</c> for the default.</param>
/// <param name="Color">One of the Adaptive Card colour names, or <c>null</c> for the default.</param>
/// <param name="Wrap">Whether long text wraps rather than being cut off.</param>
public sealed record AdaptiveTextBlock(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("weight"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Weight,
    [property: JsonPropertyName("size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Size,
    [property: JsonPropertyName("color"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Color,
    [property: JsonPropertyName("wrap")] bool Wrap)
{
    /// <summary>Always <c>TextBlock</c>.</summary>
    [JsonPropertyName("type")]
    public string Type => "TextBlock";
}

/// <summary>
/// A table of labelled values on an Adaptive Card.
/// </summary>
/// <param name="Facts">The rows.</param>
public sealed record AdaptiveFactSet(
    [property: JsonPropertyName("facts")] IReadOnlyList<AdaptiveFact> Facts)
{
    /// <summary>Always <c>FactSet</c>.</summary>
    [JsonPropertyName("type")]
    public string Type => "FactSet";
}

/// <summary>
/// One row of an <see cref="AdaptiveFactSet"/>.
/// </summary>
/// <param name="Title">The label.</param>
/// <param name="Value">The value.</param>
public sealed record AdaptiveFact(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("value")] string Value);
