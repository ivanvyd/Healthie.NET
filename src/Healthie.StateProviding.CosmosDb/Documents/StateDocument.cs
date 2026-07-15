namespace Healthie.StateProviding.CosmosDb.Documents;

/// <summary>
/// Represents a CosmosDB document that stores pulse checker state.
/// </summary>
/// <typeparam name="TState">The type of state being stored.</typeparam>
internal class StateDocument<TState>
{
    /// <summary>
    /// Gets or sets the document identifier, which corresponds to the pulse checker name.
    /// Uses lowercase <c>id</c> as required by the CosmosDB SDK.
    /// </summary>
    public string id { get; set; }

    /// <summary>
    /// Gets or sets the stored state value.
    /// </summary>
    public TState? Value { get; set; }

    /// <summary>
    /// Gets or sets the name of the stored state's type, compared against the requested type on read
    /// so a state written as one type is never returned as another.
    /// Documents written before this property existed deserialize to <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This records <see cref="System.Type.FullName"/> rather than
    /// <see cref="System.Type.AssemblyQualifiedName"/>, because the assembly-qualified name embeds
    /// the assembly version and this library's assembly version changes with every release.
    /// Recording it would make state written by one release unreadable by the next. Releases up to
    /// 2.3.0 wrote the assembly-qualified name, which begins with the full name.
    /// </remarks>
    public string? StateType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StateDocument{TState}"/> class.
    /// </summary>
    /// <param name="name">The pulse checker name used as the document identifier.</param>
    /// <param name="state">The state to store.</param>
    public StateDocument(string name, TState state)
    {
        id = name;
        Value = state;
        StateType = typeof(TState).FullName;
    }
}
