using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using System.Net;

namespace Healthie.Checkers;

/// <summary>
/// Reports whether an HTTP endpoint answers, and answers acceptably.
/// </summary>
/// <remarks>
/// <para>
/// The client comes from <see cref="IHttpClientFactory"/> under a named registration, so its
/// timeout, retry policy, proxy and certificate handling are the application's to configure -- a
/// library that constructed its own <see cref="HttpClient"/> would take those decisions away and
/// leak sockets besides.
/// </para>
/// <para>
/// The request is a GET by default and reads only the headers. A health endpoint returning a body
/// is common, and downloading it to throw it away is work this check does not need to do.
/// </para>
/// </remarks>
public sealed class HttpPulseChecker : PulseChecker
{
    /// <summary>The name this checker resolves its <see cref="HttpClient"/> under.</summary>
    public const string HttpClientName = "Healthie.Http";

    private readonly IHttpClientFactory _clients;
    private readonly Uri _url;
    private readonly HttpMethod _method;
    private readonly Func<HttpStatusCode, bool> _isAcceptable;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="HttpPulseChecker"/> class.</summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="clients">The factory the request's client comes from.</param>
    /// <param name="name">The checker's name, which identifies it in storage and on the dashboard.</param>
    /// <param name="url">The endpoint to request.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="isAcceptable">
    /// Decides whether a status counts as healthy. Defaults to any 2xx -- a 3xx is not a failure
    /// but it is not an answer either, and a health endpoint that redirects is usually misconfigured.
    /// </param>
    /// <param name="method">The request method. Defaults to GET.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    public HttpPulseChecker(
        IStateProvider stateProvider,
        IHttpClientFactory clients,
        string name,
        Uri url,
        PulseSchedule schedule,
        Func<HttpStatusCode, bool>? isAcceptable = null,
        HttpMethod? method = null,
        uint unhealthyThreshold = 0)
        : base(stateProvider, schedule, unhealthyThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(url);

        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _name = name;
        _url = url;
        _method = method ?? HttpMethod.Get;
        _isAcceptable = isAcceptable ?? (status => (int)status is >= 200 and < 300);
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string DisplayName => _url.ToString();

    /// <inheritdoc />
    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var client = _clients.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(_method, _url);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        return _isAcceptable(response.StatusCode)
            ? new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"{_method} {_url} returned {(int)response.StatusCode}.")
            : new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"{_method} {_url} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }
}
