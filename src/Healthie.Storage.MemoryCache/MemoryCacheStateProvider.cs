using Microsoft.Extensions.Caching.Memory;

namespace Healthie.Storage.MemoryCache;

public class MemoryCacheStateProvider(IMemoryCache memoryCache) : IStateProvider, IAsyncStateProvider
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public TState? GetState<TState>(string name)
    {
        return _memoryCache.Get<TState>(name);
    }

    public Task<TState?> GetStateAsync<TState>(string name)
    {
        return Task.FromResult(GetState<TState>(name));
    }

    public void SetState<TState>(string name, TState state)
    {
        _memoryCache.Set(name, state);
    }

    public Task SetStateAsync<TState>(string name, TState state)
    {
        SetState(name, state);

        return Task.CompletedTask;
    }
}
