using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IAsyncState
{
    Task<State> GetStateAsync();
    Task SetStateAsync(State state);
}
