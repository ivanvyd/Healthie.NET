using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IAsyncPulseChecker : IAsyncPulse, IAsyncState, IAsyncDisposable
{
    string Name { get; }
    Task<PulseCheckerResult> CheckAsync();
    Task SetIntervalAsync(PulseInterval interval);
    Task<bool> StopAsync();
    Task<bool> StartAsync();
}