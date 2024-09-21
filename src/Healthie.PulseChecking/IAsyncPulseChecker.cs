using Healthie.PulseChecking.Models;

namespace Healthie.PulseChecking;

public interface IAsyncPulseChecker : IAsyncPulse, IAsyncState
{
    string Name { get; }
    Task<Pulse<Result>> CheckAsync();
}