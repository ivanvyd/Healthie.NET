using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Scheduling;
using Healthie.Checkers;
using Healthie.DependencyInjection;
using System.Net;
using System.Net.Sockets;

namespace Healthie.Tests.Unit;

/// <summary>
/// Driven against real sockets, a real resolver and a real drive rather than mocks. These checkers
/// exist precisely so nobody has to write the fiddly bit, and a mocked socket would test the mock.
/// Everything here uses a loopback listener or a name that cannot resolve, so nothing reaches the
/// network and the suite still runs on a bare machine.
/// </summary>
public class BuiltInCheckerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PulseSchedule Daily = PulseSchedule.Cron("0 3 * * *");

    [Fact]
    public async Task TcpChecker_AgainstAListeningPort_IsHealthy()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            using var checker = new TcpPortPulseChecker(
                new InMemoryStateProvider(), "tcp-up", "127.0.0.1", port, Daily);

            var result = await checker.CheckAsync(Ct);

            Assert.Equal(PulseCheckerHealth.Healthy, result.Health);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// A port nobody is listening on is refused rather than left hanging, so this exercises the
    /// refusal path. The timeout path is the one the checker's own token guards; both end unhealthy.
    /// </summary>
    [Fact]
    public async Task TcpChecker_AgainstAClosedPort_IsNotHealthy()
    {
        // Bound and immediately released, so the port is almost certainly free and nothing listens.
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var checker = new TcpPortPulseChecker(
            new InMemoryStateProvider(), "tcp-down", "127.0.0.1", port, Daily, TimeSpan.FromSeconds(2));

        // A refused connection surfaces as a SocketException from CheckAsync; TriggerAsync is what
        // turns a throwing check into an unhealthy result, so this goes through the real path.
        await checker.TriggerAsync(Ct);
        var state = await checker.GetStateAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult!.Health);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void TcpChecker_WithAPortOutsideTheValidRange_IsRefused(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TcpPortPulseChecker(
            new InMemoryStateProvider(), "bad-port", "127.0.0.1", port, Daily));
    }

    [Fact]
    public async Task DnsChecker_ForALocalhostName_ResolvesAndIsHealthy()
    {
        using var checker = new DnsPulseChecker(new InMemoryStateProvider(), "dns-ok", "localhost", Daily);

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Healthy, result.Health);
    }

    [Fact]
    public async Task DnsChecker_ForANameThatCannotResolve_IsNotHealthy()
    {
        // .invalid is reserved by RFC 2606 precisely so it can never resolve.
        using var checker = new DnsPulseChecker(
            new InMemoryStateProvider(), "dns-bad", "healthie-does-not-exist.invalid", Daily);

        await checker.TriggerAsync(Ct);
        var state = await checker.GetStateAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, state.LastResult!.Health);
    }

    /// <summary>
    /// A resolver that answers successfully with no addresses is not an error it reports, and it
    /// leaves every caller holding a name it cannot connect to. Unreachable through Dns itself --
    /// a name that does not exist throws instead -- so the decision is exercised directly.
    /// </summary>
    [Fact]
    public void DnsChecker_WhenALookupSucceedsWithNoAddresses_IsUnhealthy()
    {
        var result = DnsPulseChecker.Evaluate("empty.example", []);

        Assert.Equal(PulseCheckerHealth.Unhealthy, result.Health);
    }

    [Fact]
    public void DnsChecker_WhenALookupReturnsAddresses_IsHealthy()
    {
        var result = DnsPulseChecker.Evaluate("ok.example", [IPAddress.Loopback]);

        Assert.Equal(PulseCheckerHealth.Healthy, result.Health);
    }

    [Fact]
    public async Task DiskSpaceChecker_WithThresholdsBelowWhatIsFree_IsHealthy()
    {
        var drive = DriveInfo.GetDrives().First(d => d.IsReady);

        using var checker = new DiskSpacePulseChecker(
            new InMemoryStateProvider(), "disk-ok", drive.Name, Daily,
            warnBelowBytes: 2, criticalBelowBytes: 1);

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Healthy, result.Health);
    }

    /// <summary>
    /// The warning band is the point of this checker, so it has to be reachable -- a threshold set
    /// above the free space must report suspicious, not jump to unhealthy.
    /// </summary>
    [Fact]
    public async Task DiskSpaceChecker_BelowTheWarningThresholdOnly_IsSuspicious()
    {
        var drive = DriveInfo.GetDrives().First(d => d.IsReady);

        using var checker = new DiskSpacePulseChecker(
            new InMemoryStateProvider(), "disk-warn", drive.Name, Daily,
            warnBelowBytes: long.MaxValue, criticalBelowBytes: 1);

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Suspicious, result.Health);
    }

    [Fact]
    public async Task DiskSpaceChecker_BelowTheCriticalThreshold_IsUnhealthy()
    {
        var drive = DriveInfo.GetDrives().First(d => d.IsReady);

        using var checker = new DiskSpacePulseChecker(
            new InMemoryStateProvider(), "disk-critical", drive.Name, Daily,
            warnBelowBytes: long.MaxValue, criticalBelowBytes: long.MaxValue - 1);

        var result = await checker.CheckAsync(Ct);

        Assert.Equal(PulseCheckerHealth.Unhealthy, result.Health);
    }

    /// <summary>
    /// Inverted thresholds would report unhealthy before suspicious, so the warning band the two
    /// thresholds exist to give could never be reached.
    /// </summary>
    [Fact]
    public void DiskSpaceChecker_WithACriticalThresholdAboveTheWarningOne_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new DiskSpacePulseChecker(
            new InMemoryStateProvider(), "inverted", "C:\\", Daily,
            warnBelowBytes: 1024, criticalBelowBytes: 2048));
    }

    /// <summary>
    /// These are registered by hand rather than found by scanning, and the same type is normally
    /// registered several times, so the name is what keeps them apart. A checker seeded on a
    /// schedule the interval enum cannot express has to keep it.
    /// </summary>
    [Fact]
    public async Task ACheckerSeededWithACronSchedule_KeepsIt()
    {
        using var checker = new CertificateExpiryPulseChecker(
            new InMemoryStateProvider(), "cert", "example.com", Daily);

        var state = await checker.GetStateAsync(Ct);

        Assert.Equal("0 3 * * *", state.Schedule?.CronExpression);
        Assert.Equal("0 3 * * *", state.EffectiveSchedule.CronExpression);
    }

    [Fact]
    public async Task ACheckerSeededWithAPeriodBeyondTheEnum_KeepsIt()
    {
        using var checker = new DnsPulseChecker(
            new InMemoryStateProvider(), "six-hourly", "localhost", PulseSchedule.Every(TimeSpan.FromHours(6)));

        var state = await checker.GetStateAsync(Ct);

        Assert.Equal(TimeSpan.FromHours(6), state.EffectiveSchedule.Period);
    }

    [Fact]
    public void EveryChecker_RefusesABlankName()
    {
        var states = new InMemoryStateProvider();

        Assert.Throws<ArgumentException>(() => new DnsPulseChecker(states, "  ", "localhost", Daily));
        Assert.Throws<ArgumentException>(() => new TcpPortPulseChecker(states, "", "localhost", 80, Daily));
        Assert.Throws<ArgumentException>(() => new CertificateExpiryPulseChecker(states, "", "example.com", Daily));
        Assert.Throws<ArgumentException>(() => new DiskSpacePulseChecker(states, "", "C:\\", Daily));
    }
}
