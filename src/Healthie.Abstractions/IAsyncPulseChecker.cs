using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IAsyncPulseChecker : IAsyncPulse, IAsyncState, IAsyncDisposable
{
    string Name { get; }
    Task<Pulse<Result>> CheckAsync();
    Task SetIntervalAsync(PulseInterval interval);
}