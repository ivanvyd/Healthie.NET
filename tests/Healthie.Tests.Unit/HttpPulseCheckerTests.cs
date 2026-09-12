using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Checkers;
using Healthie.DependencyInjection;
using System.Net;

namespace Healthie.Tests.Unit;

public class HttpPulseCheckerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StatusHandler(HttpStatusCode statusCode, string reasonPhrase) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode) { ReasonPhrase = reasonPhrase });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException($"Could not reach {request.RequestUri}."));
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, PulseCheckerHealth.Healthy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PulseCheckerHealth.Unhealthy)]
    public async Task DisplayAndResults_DoNotExposeCredentialsOrQueryParameters(
        HttpStatusCode statusCode,
        PulseCheckerHealth expectedHealth)
    {
        const string user = "private-user";
        const string password = "private-password";
        const string querySecret = "private-query-secret";
        const string fragmentSecret = "private-fragment-secret";
        var url = new Uri(
            $"https://{user}:{password}@example.test:8443/health/status?api_key={querySecret}#{fragmentSecret}");
        using var handler = new StatusHandler(
            statusCode,
            $"The server echoed {user} {password} {querySecret} {fragmentSecret} api_key");
        using var checker = new HttpPulseChecker(
            new InMemoryStateProvider(),
            new ClientFactory(handler),
            "credentialed-http-check",
            url,
            PulseSchedule.Every(TimeSpan.FromMinutes(1)));

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(expectedHealth, result.Health);
        Assert.Equal("https://example.test:8443/health/status", checker.DisplayName);
        Assert.Contains(checker.DisplayName, result.Message, StringComparison.Ordinal);
        foreach (var secret in new[] { user, password, querySecret, fragmentSecret, "api_key" })
        {
            Assert.DoesNotContain(secret, checker.DisplayName, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TransportException_DoesNotExposeTheRequestUriInPersistedState()
    {
        const string user = "private-user";
        const string password = "private-password";
        const string querySecret = "private-query-secret";
        const string fragmentSecret = "private-fragment-secret";
        var url = new Uri(
            $"https://{user}:{password}@example.test/health?api_key={querySecret}#{fragmentSecret}");
        var stateProvider = new InMemoryStateProvider();
        using var handler = new ThrowingHandler();
        using var checker = new HttpPulseChecker(
            stateProvider,
            new ClientFactory(handler),
            "throwing-http-check",
            url,
            PulseSchedule.Every(TimeSpan.FromMinutes(1)));

        await checker.TriggerAsync(Ct);
        var state = await stateProvider.GetStateAsync<PulseCheckerState>(checker.Name, Ct);

        Assert.NotNull(state?.LastResult);
        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult.Health);
        Assert.Equal("GET https://example.test/health request failed.", state.LastResult.Message);
        foreach (var secret in new[] { user, password, querySecret, fragmentSecret, "api_key" })
        {
            Assert.DoesNotContain(secret, state.LastResult.Message, StringComparison.Ordinal);
        }
    }
}
