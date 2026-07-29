using Healthie.Abstractions.StateProviding;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Healthie.DependencyInjection;

/// <summary>
/// An <see cref="IStateProvider"/> that keeps pulse checker state in process memory.
/// This is the default provider registered by <c>AddHealthie</c>.
/// </summary>
/// <remarks>
/// <para>
/// State is lost when the process restarts and is not shared between instances.
/// Register a durable provider such as <c>AddHealthieCosmosDb</c> for production
/// or multi-instance deployments.
/// </para>
/// <para>
/// State is held as serialized JSON so that reads return an independent copy, matching the
/// behavior of durable providers. Mutating a returned state therefore has no effect on what
/// is stored until it is passed back to <see cref="SetStateAsync{TState}"/>.
/// </para>
/// </remarks>
public sealed class InMemoryStateProvider : IStateProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General);

    private readonly ConcurrentDictionary<string, string> _states = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return _states.TryGetValue(name, out var json)
            ? Task.FromResult(JsonSerializer.Deserialize<TState>(json, SerializerOptions))
            : Task.FromResult<TState?>(default);
    }

    /// <inheritdoc />
    public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        _states[name] = JsonSerializer.Serialize(state, SerializerOptions);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteStateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_states.TryRemove(name, out _));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads straight from the dictionary. There is no round trip to save here, but the default
    /// walks the same store one name at a time for no reason, and this is the provider every
    /// application starts on.
    /// </remarks>
    public Task<IReadOnlyDictionary<string, TState>> GetStatesAsync<TState>(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        cancellationToken.ThrowIfCancellationRequested();

        var states = new Dictionary<string, TState>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (_states.TryGetValue(name, out var json)
                && JsonSerializer.Deserialize<TState>(json, SerializerOptions) is { } state)
            {
                states[name] = state;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, TState>>(states);
    }
}
