using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Healthie.Checkers;

/// <summary>
/// Reports how long a TLS certificate has left, and turns suspicious before it expires rather than
/// after.
/// </summary>
/// <remarks>
/// <para>
/// The three-state model earns its keep here more than anywhere else. An expiry is not a blip and
/// not a surprise: it is a date known months ahead, and the useful signal is the warning, not the
/// outage. So the checker reports suspicious once the certificate is inside
/// <see cref="WarnWithin"/>, and unhealthy only once it has actually expired or cannot be read.
/// </para>
/// <para>
/// Checking daily is the natural schedule, which is why this checker needed
/// <see cref="PulseSchedule"/> to exist -- the interval enum stops at five minutes.
/// </para>
/// </remarks>
public sealed class CertificateExpiryPulseChecker : PulseChecker
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _warnWithin;
    private readonly TimeSpan _timeout;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="CertificateExpiryPulseChecker"/> class.</summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="name">The checker's name, which identifies it in storage and on the dashboard.</param>
    /// <param name="host">The host whose certificate to read.</param>
    /// <param name="schedule">How often to check. Daily is usually right.</param>
    /// <param name="warnWithin">How long before expiry to start reporting suspicious. Defaults to 30 days.</param>
    /// <param name="port">The TLS port. Defaults to 443.</param>
    /// <param name="timeout">How long to wait for the handshake. Defaults to ten seconds.</param>
    public CertificateExpiryPulseChecker(
        IStateProvider stateProvider,
        string name,
        string host,
        PulseSchedule schedule,
        TimeSpan? warnWithin = null,
        int port = 443,
        TimeSpan? timeout = null)
        : base(stateProvider, schedule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        _name = name;
        _host = host;
        _port = port;
        _warnWithin = warnWithin ?? TimeSpan.FromDays(30);
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>How long before expiry this checker starts reporting suspicious.</summary>
    public TimeSpan WarnWithin => _warnWithin;

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string DisplayName => _host;

    /// <inheritdoc />
    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);

        // Validation is accepted unconditionally so that expiry is reported rather than thrown.
        // A certificate that has already lapsed fails the default callback, and this checker's
        // entire job is to say how long is left -- including when the answer is "none".
        using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);

        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = _host },
            timeout.Token).ConfigureAwait(false);

        if (tls.RemoteCertificate is not { } certificate)
        {
            return new PulseCheckerResult(PulseCheckerHealth.Unhealthy, $"{_host} presented no certificate.");
        }

        using var x509 = new X509Certificate2(certificate);
        var expiresAt = x509.NotAfter.ToUniversalTime();
        var remaining = expiresAt - DateTime.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"Certificate for {_host} expired on {expiresAt:u}.");
        }

        if (remaining <= _warnWithin)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Certificate for {_host} expires in {remaining.Days} days, on {expiresAt:u}.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Healthy,
            $"Certificate for {_host} is valid for another {remaining.Days} days.");
    }
}
