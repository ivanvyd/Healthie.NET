using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IAsyncPulseChecker : IAsyncPulse, IAsyncState
{
    string Name { get; }
    Task<Pulse<Result>> CheckAsync();
}