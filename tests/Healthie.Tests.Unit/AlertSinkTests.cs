using Healthie.Abstractions.Enums;
using Healthie.Alerting;
using System.Net;
using System.Text.Json;

namespace Healthie.Tests.Unit;

/// <summary>
/// What each sink actually puts on the wire.
/// </summary>
/// <remarks>
/// Asserted against the request body rather than against the payload records, because the thing that
/// breaks is the shape a service receives: Slack, Teams and PagerDuty each reject arbitrary JSON, so
/// a field named right in C# and wrong in JSON fails only at the far end, where nothing here would
/// see it.
/// </remarks>
public class AlertSinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Alert Failing(
        PulseCheckerHealth current = PulseCheckerHealth.Unhealthy,
        PulseCheckerHealth? previous = PulseCheckerHealth.Healthy) =>
        new(
            "Acme.Checkers.PaymentsPulseChecker",
            "Payments API",
            "Tier 1",
            ["payments", "external"],
            previous,
            current,
            "502 Bad Gateway",
            new DateTime(2026, 7, 29, 14, 30, 0, DateTimeKind.Utc));

    private static Alert Recovered() => Failing(PulseCheckerHealth.Healthy, PulseCheckerHealth.Unhealthy);

    /// <summary>Captures the one request a sink makes, and answers it.</summary>
    private sealed class CapturingHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (SingleClientFactory Clients, CapturingHandler Handler) Capture(
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(status);
        return (new SingleClientFactory(handler), handler);
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;


    [Fact]
    public async Task Slack_SendsTheShapeAnIncomingWebhookAccepts()
    {
        var (clients, handler) = Capture();
        var sink = new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc"));

        await sink.SendAsync(Failing(), Ct);

        var root = Json(handler.Body);

        // `text` is what a push notification shows, so it must carry the headline on its own.
        Assert.Contains("Payments API", root.GetProperty("text").GetString()!, StringComparison.Ordinal);

        var attachment = root.GetProperty("attachments")[0];
        Assert.Equal("danger", attachment.GetProperty("color").GetString());
        Assert.Equal("Acme.Checkers.PaymentsPulseChecker", attachment.GetProperty("footer").GetString());

        var fields = attachment.GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("title").GetString()!, f => f.GetProperty("value").GetString()!);

        Assert.Equal("Unhealthy", fields["Status"]);
        Assert.Equal("Healthy", fields["Was"]);
        Assert.Equal("Tier 1", fields["Group"]);
        Assert.Equal("payments, external", fields["Tags"]);
        Assert.Equal("502 Bad Gateway", fields["Message"]);
    }

    [Theory]
    [InlineData(PulseCheckerHealth.Unhealthy, "danger")]
    [InlineData(PulseCheckerHealth.Suspicious, "warning")]
    [InlineData(PulseCheckerHealth.Healthy, "good")]
    public async Task Slack_ColoursTheAttachmentByHealth(PulseCheckerHealth health, string expected)
    {
        var (clients, handler) = Capture();
        var sink = new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc"));

        await sink.SendAsync(Failing(health, PulseCheckerHealth.Suspicious), Ct);

        Assert.Equal(expected, Json(handler.Body).GetProperty("attachments")[0].GetProperty("color").GetString());
    }

    /// <summary>
    /// An alert's time is UTC. Read as anything else it is shifted by the server's offset, and the
    /// message says a component failed at a time it did not.
    /// </summary>
    /// <remarks>
    /// The time here carries <see cref="DateTimeKind.Unspecified"/>, which is what makes this test
    /// mean something: <c>new DateTimeOffset(DateTime)</c> takes its offset from the Kind, so a time
    /// already marked Utc converts correctly whether or not the sink says so. Only an unmarked one
    /// -- a state round-tripped through a store that does not preserve Kind -- tells the two apart.
    /// </remarks>
    [Fact]
    public async Task Slack_TimestampsInUtcEvenWhenTheTimeDoesNotSaySo()
    {
        var (clients, handler) = Capture();
        var sink = new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc"));

        var unmarked = Failing() with
        {
            OccurredAt = new DateTime(2026, 7, 29, 14, 30, 0, DateTimeKind.Unspecified),
        };

        await sink.SendAsync(unmarked, Ct);

        var expected = new DateTimeOffset(2026, 7, 29, 14, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        Assert.Equal(expected, Json(handler.Body).GetProperty("attachments")[0].GetProperty("ts").GetInt64());
    }

    /// <summary>
    /// Slack parses the top-level text as mrkdwn, so an unescaped angle bracket starts what it takes
    /// for a link. DisplayName is virtual and "Auth &amp; Session" is an ordinary name to give a checker.
    /// </summary>
    [Fact]
    public async Task Slack_EscapesTheCharactersItWouldOtherwiseReadAsMarkup()
    {
        var (clients, handler) = Capture();
        var sink = new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc"));

        var awkward = Failing() with { DisplayName = "Auth & <Session>" };

        await sink.SendAsync(awkward, Ct);

        var text = Json(handler.Body).GetProperty("text").GetString()!;

        Assert.Contains("Auth &amp; &lt;Session&gt;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<Session>", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A checker with no group must not send <c>"group": null</c> -- every other optional field here
    /// is omitted when it is absent, and this one was not.
    /// </summary>
    [Fact]
    public async Task PagerDuty_OmitsTheGroupWhenTheCheckerHasNone()
    {
        var (clients, handler) = Capture(HttpStatusCode.Accepted);
        var sink = new PagerDutyAlertSink(clients, "routing-key-1");

        await sink.SendAsync(Failing() with { Group = null }, Ct);

        Assert.False(Json(handler.Body).GetProperty("payload").TryGetProperty("group", out _));
    }


    [Fact]
    public async Task Teams_SendsAnAdaptiveCardInTheEnvelopeWorkflowsExpects()
    {
        var (clients, handler) = Capture();
        var sink = new MicrosoftTeamsAlertSink(clients, new Uri("https://prod.westeurope.logic.azure.test/workflows/abc"));

        await sink.SendAsync(Failing(), Ct);

        var root = Json(handler.Body);
        Assert.Equal("message", root.GetProperty("type").GetString());

        var attachment = root.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());

        var card = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", card.GetProperty("type").GetString());

        // Teams refuses a card with no schema.
        Assert.Equal("http://adaptivecards.io/schemas/adaptive-card.json", card.GetProperty("$schema").GetString());

        var body = card.GetProperty("body").EnumerateArray().ToList();
        Assert.Equal("TextBlock", body[0].GetProperty("type").GetString());
        Assert.Equal("attention", body[0].GetProperty("color").GetString());
        Assert.Contains("Payments API", body[0].GetProperty("text").GetString()!, StringComparison.Ordinal);

        Assert.Equal("FactSet", body[1].GetProperty("type").GetString());

        var facts = body[1].GetProperty("facts").EnumerateArray()
            .ToDictionary(f => f.GetProperty("title").GetString()!, f => f.GetProperty("value").GetString()!);

        Assert.Equal("Unhealthy", facts["Status"]);
        Assert.Equal("Healthy", facts["Was"]);
        Assert.Contains("UTC", facts["Observed"], StringComparison.Ordinal);
    }

    /// <summary>
    /// A card element carrying a null weight or colour is invalid to Teams, so the optional ones are
    /// left out rather than written as null.
    /// </summary>
    [Fact]
    public async Task Teams_OmitsOptionalCardPropertiesRatherThanWritingThemNull()
    {
        var (clients, handler) = Capture();
        var sink = new MicrosoftTeamsAlertSink(clients, new Uri("https://prod.westeurope.logic.azure.test/workflows/abc"));

        await sink.SendAsync(Failing(), Ct);

        var body = Json(handler.Body)
            .GetProperty("attachments")[0].GetProperty("content").GetProperty("body")
            .EnumerateArray().ToList();

        // The message block is the plain one: no weight, size or colour of its own.
        var message = body.Last();
        Assert.False(message.TryGetProperty("weight", out _));
        Assert.False(message.TryGetProperty("size", out _));
        Assert.False(message.TryGetProperty("color", out _));
        Assert.True(message.GetProperty("wrap").GetBoolean());
    }


    [Fact]
    public async Task PagerDuty_TriggersAnIncidentKeyedOnTheChecker()
    {
        var (clients, handler) = Capture(HttpStatusCode.Accepted);
        var sink = new PagerDutyAlertSink(clients, "routing-key-1");

        var alert = Failing();
        await sink.SendAsync(alert, Ct);

        var root = Json(handler.Body);
        Assert.Equal("routing-key-1", root.GetProperty("routing_key").GetString());
        Assert.Equal("trigger", root.GetProperty("event_action").GetString());
        Assert.Equal(alert.DeduplicationKey, root.GetProperty("dedup_key").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal("critical", payload.GetProperty("severity").GetString());
        Assert.Equal("Acme.Checkers.PaymentsPulseChecker", payload.GetProperty("source").GetString());
        Assert.Equal("Payments API", payload.GetProperty("component").GetString());
        Assert.Equal("Tier 1", payload.GetProperty("group").GetString());
        Assert.Equal("502 Bad Gateway", payload.GetProperty("custom_details").GetProperty("message").GetString());
    }

    /// <summary>
    /// The reason the deduplication key leaves out the health and the time: a recovery has to close
    /// the incident the failure opened, not open a second one saying everything is fine.
    /// </summary>
    [Fact]
    public async Task PagerDuty_ResolvesTheSameIncidentOnRecovery()
    {
        var (clients, handler) = Capture(HttpStatusCode.Accepted);
        var sink = new PagerDutyAlertSink(clients, "routing-key-1");

        await sink.SendAsync(Failing(), Ct);
        var opened = Json(handler.Body).GetProperty("dedup_key").GetString();

        await sink.SendAsync(Recovered(), Ct);
        var root = Json(handler.Body);

        Assert.Equal("resolve", root.GetProperty("event_action").GetString());
        Assert.Equal(opened, root.GetProperty("dedup_key").GetString());

        // A resolve carries no payload at all -- not one written as null.
        Assert.False(root.TryGetProperty("payload", out _));
    }

    /// <summary>
    /// Suspicious means "failing, but not past the threshold yet". Paging on it would defeat having
    /// a threshold at all.
    /// </summary>
    [Theory]
    [InlineData(PulseCheckerHealth.Unhealthy, "critical")]
    [InlineData(PulseCheckerHealth.Suspicious, "warning")]
    public async Task PagerDuty_MapsHealthOntoItsOwnSeverityScale(PulseCheckerHealth health, string expected)
    {
        var (clients, handler) = Capture(HttpStatusCode.Accepted);
        var sink = new PagerDutyAlertSink(clients, "routing-key-1");

        await sink.SendAsync(Failing(health, PulseCheckerHealth.Healthy), Ct);

        Assert.Equal(expected, Json(handler.Body).GetProperty("payload").GetProperty("severity").GetString());
    }

    [Fact]
    public async Task PagerDuty_PostsToTheEventsApiUnlessToldOtherwise()
    {
        var (clients, handler) = Capture(HttpStatusCode.Accepted);

        await new PagerDutyAlertSink(clients, "routing-key-1").SendAsync(Failing(), Ct);
        Assert.Equal(PagerDutyAlertSink.EventsEndpoint, handler.Request!.RequestUri);

        var eu = new Uri("https://events.eu.pagerduty.com/v2/enqueue");
        await new PagerDutyAlertSink(clients, "routing-key-1", eu).SendAsync(Failing(), Ct);
        Assert.Equal(eu, handler.Request!.RequestUri);
    }

    [Fact]
    public void PagerDuty_WithoutARoutingKey_SaysSoAtConstruction()
    {
        var (clients, _) = Capture();

        Assert.Throws<ArgumentException>(() => new PagerDutyAlertSink(clients, "  "));
    }


    /// <summary>
    /// Throwing is how a sink reports a failed delivery: the dispatcher catches it, logs it, and no
    /// check is affected. A sink that swallowed the failure would report a delivery that never
    /// happened.
    /// </summary>
    [Fact]
    public async Task ARejectedDelivery_Throws()
    {
        var (clients, _) = Capture(HttpStatusCode.BadRequest);

        var sinks = new IAlertSink[]
        {
            new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc")),
            new MicrosoftTeamsAlertSink(clients, new Uri("https://prod.westeurope.logic.azure.test/workflows/abc")),
            new PagerDutyAlertSink(clients, "routing-key-1"),
            new WebhookAlertSink(clients, new Uri("https://example.test/hook")),
        };

        foreach (var sink in sinks)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => sink.SendAsync(Failing(), Ct));
        }
    }

    [Fact]
    public async Task EverySink_PostsRatherThanAnythingElse()
    {
        var (clients, handler) = Capture();

        foreach (var sink in new IAlertSink[]
        {
            new SlackAlertSink(clients, new Uri("https://hooks.slack.test/services/abc")),
            new MicrosoftTeamsAlertSink(clients, new Uri("https://prod.westeurope.logic.azure.test/workflows/abc")),
            new WebhookAlertSink(clients, new Uri("https://example.test/hook")),
        })
        {
            await sink.SendAsync(Failing(), Ct);

            Assert.Equal(HttpMethod.Post, handler.Request!.Method);
            Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        }
    }
}
