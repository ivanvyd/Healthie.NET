using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using System.Net.Sockets;

namespace Healthie.Checkers;

/// <summary>
/// Reports whether a TCP port accepts a connection.
/// </summary>
/// <remarks>
/// The least a check can ask of a dependency that speaks no HTTP -- a database, a broker, an SMTP
/// relay. It proves the port is listening and reachable, and nothing about whether the service
/// behind it is well; for that, check the thing itself.
/// </remarks>
public sealed class TcpPortPulseChecker : PulseChecker
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="TcpPortPulseChecker"/> class.</summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="name">The checker's name, which identifies it in storage and on the dashboard.</param>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="timeout">How long to wait for the connection. Defaults to five seconds.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    public TcpPortPulseChecker(
        IStateProvider stateProvider,
        string name,
        string host,
        int port,
        PulseSchedule schedule,
        TimeSpan? timeout = null,
        uint unhealthyThreshold = 0)
        : base(stateProvider, schedule, unhealthyThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        _name = name;
        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string DisplayName => $"{_host}:{_port}";

    /// <inheritdoc />
    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();

        // The timeout is its own token rather than a socket option: a connection that hangs rather
        // than being refused is the interesting failure, and it is the one a bare ConnectAsync
        // waits out for the operating system's full retry period.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);

            return new PulseCheckerResult(PulseCheckerHealth.Healthy, $"Connected to {_host}:{_port}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"{_host}:{_port} did not accept a connection within {_timeout.TotalSeconds:0.#}s.");
        }
    }
}
