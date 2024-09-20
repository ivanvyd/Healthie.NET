using Microsoft.Extensions.Caching.Memory;

namespace Healthie.Storage.MemoryCache;

public class MemoryCacheStateProvider(IMemoryCache memoryCache) : IStateProvider
{
    private readonly IMemoryCache _memoryCache = memoryCache;

    public TState? GetState<TState>(string name)
    {
        return _memoryCache.Get<TState>(name);
    }

    public void SetState<TState>(string name, TState state)
    {
        _memoryCache.Set(name, state);
    }
}
