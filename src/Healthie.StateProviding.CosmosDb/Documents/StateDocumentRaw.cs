using System.Text.Json;

namespace Healthie.StateProviding.CosmosDb.Documents;

internal class StateDocumentRaw
{
    public string id { get; set; } = null!;
    public JsonElement Value { get; set; }
    public string StateType { get; set; } = null!;
}