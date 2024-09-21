using Healthie.PulseChecking.Models;

namespace Healthie.PulseChecking;

public interface IAsyncState
{
    Task<State> GetStateAsync();
    Task SetStateAsync(State state);
}
