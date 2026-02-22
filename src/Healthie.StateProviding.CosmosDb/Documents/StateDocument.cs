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
    /// Gets or sets the assembly-qualified type name of the stored state for safe deserialization.
    /// </summary>
    public string StateType { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StateDocument{TState}"/> class.
    /// </summary>
    /// <param name="name">The pulse checker name used as the document identifier.</param>
    /// <param name="state">The state to store.</param>
    public StateDocument(string name, TState state)
    {
        id = name;
        Value = state;
        StateType = typeof(TState).AssemblyQualifiedName!;
    }
}
