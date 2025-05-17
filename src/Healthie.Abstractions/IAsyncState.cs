using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IAsyncState
{
    Task<PulseCheckerState> GetStateAsync();
    Task SetStateAsync(PulseCheckerState state);
}
