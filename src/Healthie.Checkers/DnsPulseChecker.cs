using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using System.Net;

namespace Healthie.Checkers;

/// <summary>
/// Reports whether a hostname resolves.
/// </summary>
/// <remarks>
/// Worth having on its own rather than folded into an HTTP check: when DNS is the thing that broke,
/// every check that depends on a name fails at once and none of them says why. This one does.
/// </remarks>
public sealed class DnsPulseChecker : PulseChecker
{
    private readonly string _hostname;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="DnsPulseChecker"/> class.</summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="name">The checker's name, which identifies it in storage and on the dashboard.</param>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="unhealthyThreshold">Consecutive failures before the checker is unhealthy.</param>
    public DnsPulseChecker(
        IStateProvider stateProvider,
        string name,
        string hostname,
        PulseSchedule schedule,
        uint unhealthyThreshold = 0)
        : base(stateProvider, schedule, unhealthyThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        _name = name;
        _hostname = hostname;
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string DisplayName => _hostname;

    /// <inheritdoc />
    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var addresses = await Dns.GetHostAddressesAsync(_hostname, cancellationToken).ConfigureAwait(false);

        return Evaluate(_hostname, addresses);
    }

    /// <summary>
    /// Turns a lookup's answer into a result.
    /// </summary>
    /// <remarks>
    /// Separate from the lookup so the empty-answer case can be tested. A name that does not exist
    /// makes the resolver throw, so driving this through <see cref="Dns"/> only ever reaches the
    /// throwing path -- and a resolver that answers successfully with nothing leaves every caller
    /// holding a name it cannot connect to, which is the case worth being sure about.
    /// </remarks>
    internal static PulseCheckerResult Evaluate(string hostname, IPAddress[] addresses) =>
        addresses.Length == 0
            ? new PulseCheckerResult(PulseCheckerHealth.Unhealthy, $"{hostname} resolved to no addresses.")
            : new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"{hostname} resolved to {addresses.Length} address(es), first {addresses[0]}.");
}
