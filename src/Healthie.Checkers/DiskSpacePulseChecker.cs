using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Checkers;

/// <summary>
/// Reports how much free space a drive has left, turning suspicious before it runs out.
/// </summary>
/// <remarks>
/// Two thresholds rather than one, because a disk filling up is gradual and knowable in advance --
/// the same reason certificate expiry has a warning band. Below <see cref="WarnBelow"/> it is
/// suspicious; below <see cref="CriticalBelow"/> it is unhealthy.
/// </remarks>
public sealed class DiskSpacePulseChecker : PulseChecker
{
    private const double BytesPerGigabyte = 1024d * 1024d * 1024d;

    private readonly string _driveName;
    private readonly long _warnBelow;
    private readonly long _criticalBelow;
    private readonly string _name;

    /// <summary>Initializes a new instance of the <see cref="DiskSpacePulseChecker"/> class.</summary>
    /// <param name="stateProvider">The state provider used to manage pulse checker state.</param>
    /// <param name="name">The checker's name, which identifies it in storage and on the dashboard.</param>
    /// <param name="driveName">The drive to inspect, as <see cref="DriveInfo.Name"/> gives it.</param>
    /// <param name="schedule">How often to check.</param>
    /// <param name="warnBelowBytes">Free space below which the checker is suspicious. Defaults to 10 GiB.</param>
    /// <param name="criticalBelowBytes">Free space below which the checker is unhealthy. Defaults to 2 GiB.</param>
    /// <exception cref="ArgumentException">The critical threshold is not below the warning one.</exception>
    public DiskSpacePulseChecker(
        IStateProvider stateProvider,
        string name,
        string driveName,
        PulseSchedule schedule,
        long warnBelowBytes = 10L * 1024 * 1024 * 1024,
        long criticalBelowBytes = 2L * 1024 * 1024 * 1024)
        : base(stateProvider, schedule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(driveName);

        // Inverted thresholds would report unhealthy before suspicious, and the state would jump
        // straight past the warning the two thresholds exist to give.
        if (criticalBelowBytes >= warnBelowBytes)
        {
            throw new ArgumentException(
                $"The critical threshold ({criticalBelowBytes} bytes) must be below the warning one " +
                $"({warnBelowBytes} bytes), or the warning can never be reported.",
                nameof(criticalBelowBytes));
        }

        _name = name;
        _driveName = driveName;
        _warnBelow = warnBelowBytes;
        _criticalBelow = criticalBelowBytes;
    }

    /// <summary>Free space below which this checker reports suspicious.</summary>
    public long WarnBelow => _warnBelow;

    /// <summary>Free space below which this checker reports unhealthy.</summary>
    public long CriticalBelow => _criticalBelow;

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string DisplayName => _driveName;

    /// <inheritdoc />
    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var drive = new DriveInfo(_driveName);

        if (!drive.IsReady)
        {
            return Task.FromResult(new PulseCheckerResult(
                PulseCheckerHealth.Unhealthy,
                $"Drive {_driveName} is not ready."));
        }

        var free = drive.AvailableFreeSpace;
        var summary = $"{free / BytesPerGigabyte:0.##} GiB free on {_driveName}";

        var health = free < _criticalBelow ? PulseCheckerHealth.Unhealthy
            : free < _warnBelow ? PulseCheckerHealth.Suspicious
            : PulseCheckerHealth.Healthy;

        return Task.FromResult(new PulseCheckerResult(health, summary + "."));
    }
}
