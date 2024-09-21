namespace Healthie.StateProviding;

public interface IAsyncStateProvider
{
    Task<TState?> GetStateAsync<TState>(string name);

    Task SetStateAsync<TState>(string name, TState state);
}
