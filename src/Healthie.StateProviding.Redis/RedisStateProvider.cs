using Healthie.Abstractions.StateProviding;
using StackExchange.Redis;
using System.Text.Json;

namespace Healthie.StateProviding.Redis;

/// <summary>
/// Stores pulse checker state in Redis.
/// </summary>
/// <remarks>
/// <para>
/// The fastest of the durable providers, because state is written on every tick of every checker and
/// Redis is the store that does not mind. A relational provider does a round trip to a disk-backed
/// engine for each of those writes; this does one to memory.
/// </para>
/// <para>
/// One hash per checker, holding the state, the type it was written as, and a version. A hash rather
/// than a plain string so the version can be compared and swapped in the same command as the write
/// -- <see cref="TrySetStateAsync"/> is a Lua script, which Redis runs to completion without
/// interleaving anything else, so the compare and the write cannot come apart.
/// </para>
/// </remarks>
public sealed class RedisStateProvider : IStateProvider
{
    private const string ValueField = "value";
    private const string TypeField = "state_type";
    private const string VersionField = "version";

    /// <summary>
    /// Writes only if the stored version is still the one the caller read.
    /// </summary>
    /// <remarks>
    /// Returns 1 when the write landed and 0 when it was refused, which is the same shape the
    /// relational providers get from rows-affected. Redis runs a script atomically, so nothing can
    /// change the version between the HGET and the HSET.
    /// </remarks>
    private const string ConditionalWriteScript = """
        if redis.call('HGET', KEYS[1], 'version') ~= ARGV[3] then
            return 0
        end
        redis.call('HSET', KEYS[1], 'value', ARGV[1], 'state_type', ARGV[2], 'version', ARGV[4])
        return 1
        """;

    /// <summary>
    /// Writes only if nothing is stored yet.
    /// </summary>
    /// <remarks>
    /// The create half of the guarantee: two writers both finding nothing must not both create, or
    /// one of the two changes is lost. <c>EXISTS</c> and the write are in one script for the same
    /// reason as above.
    /// </remarks>
    private const string CreateIfAbsentScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
            return 0
        end
        redis.call('HSET', KEYS[1], 'value', ARGV[1], 'state_type', ARGV[2], 'version', ARGV[3])
        return 1
        """;

    private readonly IConnectionMultiplexer _connection;
    private readonly string _keyPrefix;

    /// <summary>Initializes a new instance of the <see cref="RedisStateProvider"/> class.</summary>
    /// <param name="connection">The connection to Redis.</param>
    /// <param name="keyPrefix">Prefixed to every key this provider owns.</param>
    public RedisStateProvider(IConnectionMultiplexer connection, string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _keyPrefix = keyPrefix;
    }

    /// <inheritdoc />
    public bool SupportsOptimisticConcurrency => true;

    private IDatabase Database => _connection.GetDatabase();

    private RedisKey KeyFor(string name) => _keyPrefix + name;

    /// <summary>A fresh version for a write.</summary>
    /// <remarks>
    /// Generated here rather than by Redis, which has no per-field revision to read. It is opaque to
    /// callers, so only its uniqueness matters.
    /// </remarks>
    private static string NewVersion() => Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public async Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
    {
        var entry = await GetStateEntryAsync<TState>(name, cancellationToken).ConfigureAwait(false);

        return entry is null ? default : entry.Value;
    }

    /// <inheritdoc />
    public async Task<StateEntry<TState>?> GetStateEntryAsync<TState>(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var fields = await Database
            .HashGetAsync(KeyFor(name), [ValueField, TypeField, VersionField])
            .ConfigureAwait(false);

        if (fields[0].IsNull)
        {
            return null;
        }

        EnsureStoredTypeMatches<TState>(name, fields[1].IsNull ? null : fields[1].ToString());

        var value = JsonSerializer.Deserialize<TState>(fields[0].ToString());

        if (value is null)
        {
            return null;
        }

        // Null for a hash written before this provider versioned its writes. Reported as unversioned
        // rather than invented, so a caller writes it unconditionally as it did before.
        var version = fields[2].IsNull ? null : fields[2].ToString();

        return new StateEntry<TState>(value, version);
    }

    /// <inheritdoc />
    public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Database.HashSetAsync(
            KeyFor(name),
            [
                new HashEntry(ValueField, JsonSerializer.Serialize(state)),
                new HashEntry(TypeField, typeof(TState).FullName),
                new HashEntry(VersionField, NewVersion()),
            ]);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetStateAsync<TState>(
        string name,
        TState state,
        string? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (expectedVersion is null)
        {
            await SetStateAsync(name, state, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var json = JsonSerializer.Serialize(state);
        var type = typeof(TState).FullName;
        var version = NewVersion();

        var script = expectedVersion == IStateProvider.AbsentVersion ? CreateIfAbsentScript : ConditionalWriteScript;

        RedisValue[] arguments = expectedVersion == IStateProvider.AbsentVersion
            ? [json, type, version]
            : [json, type, expectedVersion, version];

        var result = await Database
            .ScriptEvaluateAsync(script, [KeyFor(name)], arguments)
            .ConfigureAwait(false);

        return (long)result == 1;
    }

    /// <inheritdoc />
    public Task<bool> DeleteStateAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Redis reports whether the key was there, which is the only thing a caller can act on.
        return Database.KeyDeleteAsync(KeyFor(name));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every read is issued before any is awaited. StackExchange.Redis pipelines commands on one
    /// connection, so this costs one round trip rather than one per checker -- which is the whole
    /// point of the bulk read on a dashboard that lists every checker.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, TState>> GetStatesAsync<TState>(
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        var wanted = names.Distinct(StringComparer.Ordinal).ToList();
        var states = new Dictionary<string, TState>(StringComparer.Ordinal);

        if (wanted.Count == 0)
        {
            return states;
        }

        var database = Database;

        var reads = wanted
            .Select(name => database.HashGetAsync(KeyFor(name), [ValueField, TypeField]))
            .ToList();

        var results = await Task.WhenAll(reads).ConfigureAwait(false);

        for (var i = 0; i < wanted.Count; i++)
        {
            var fields = results[i];

            if (fields[0].IsNull)
            {
                continue;
            }

            EnsureStoredTypeMatches<TState>(wanted[i], fields[1].IsNull ? null : fields[1].ToString());

            if (JsonSerializer.Deserialize<TState>(fields[0].ToString()) is { } state)
            {
                states[wanted[i]] = state;
            }
        }

        return states;
    }

    /// <summary>
    /// Refuses to return a state as a type it was not written as.
    /// </summary>
    /// <remarks>
    /// Compares the full name rather than the assembly-qualified one, because that embeds the
    /// assembly version and this library's version changes with every release -- comparing it would
    /// make state written by one release unreadable by the next. The same reasoning, and the same
    /// prefix allowance for releases that wrote the longer form, as the CosmosDB provider.
    /// </remarks>
    internal static void EnsureStoredTypeMatches<TState>(string name, string? storedStateType)
    {
        // A hash written before the type was recorded carries none to compare against.
        if (string.IsNullOrWhiteSpace(storedStateType))
        {
            return;
        }

        var expectedStateType = typeof(TState).FullName;

        if (expectedStateType is null
            || string.Equals(storedStateType, expectedStateType, StringComparison.Ordinal)
            || storedStateType.StartsWith(expectedStateType + ",", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The state stored for pulse checker '{name}' was written as '{storedStateType}', and was " +
            $"read as '{expectedStateType}'. Reading it as a different type would return a state that " +
            "is not the one that was stored.");
    }
}
