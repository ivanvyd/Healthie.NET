namespace Healthie.StateProviding.CosmosDb.Documents;

internal class StateDocument<TState>
{
    public string id { get; set; }
    public TState? Value { get; set; }
    public string StateType { get; set; }

    public StateDocument(string name, TState state)
    {
        id = name;
        Value = state;
        StateType = typeof(TState).AssemblyQualifiedName!;
    }
}