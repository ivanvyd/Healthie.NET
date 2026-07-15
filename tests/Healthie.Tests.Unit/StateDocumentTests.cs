using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.StateProviding.CosmosDb.Documents;
using Newtonsoft.Json;

namespace Healthie.Tests.Unit;

/// <summary>
/// The CosmosDB SDK serializes documents with Newtonsoft.Json by default. `StateDocument`'s only
/// constructor takes parameters that match none of the document's fields, so deserialization relies
/// entirely on its properties being settable afterwards. Making a property init-only, or turning the
/// type into a record, would leave `Value` at its default: reads would return no state, and every
/// checker would silently reset instead of failing. These tests pin the round trip.
/// </summary>
public class StateDocumentTests
{
    private static readonly DateTime ExecutedAt = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static PulseCheckerState CreateState() => new(PulseInterval.Every30Seconds, unhealthyThreshold: 2)
    {
        ConsecutiveFailureCount = 1,
        IsActive = false,
        IsHistoryEnabled = true,
        LastExecutionDateTime = ExecutedAt,
        LastResult = new PulseCheckerResult(PulseCheckerHealth.Suspicious, "flaky"),
        History = [new PulseCheckerHistoryEntry(PulseCheckerHealth.Suspicious, "flaky", ExecutedAt)],
    };

    private static StateDocument<PulseCheckerState> RoundTrip(StateDocument<PulseCheckerState> document)
    {
        var json = JsonConvert.SerializeObject(document);

        return JsonConvert.DeserializeObject<StateDocument<PulseCheckerState>>(json)!;
    }

    [Fact]
    public void RoundTrip_PreservesTheStoredState()
    {
        var original = new StateDocument<PulseCheckerState>("Some.Checker", CreateState());

        var restored = RoundTrip(original);

        Assert.NotNull(restored.Value);
        Assert.Equal(CreateState(), restored.Value);
    }

    [Fact]
    public void RoundTrip_PreservesTheDocumentIdentity()
    {
        var original = new StateDocument<PulseCheckerState>("Some.Checker", CreateState());

        var restored = RoundTrip(original);

        Assert.Equal("Some.Checker", restored.id);
    }

    [Fact]
    public void RoundTrip_PreservesTheRecordedStateType()
    {
        var original = new StateDocument<PulseCheckerState>("Some.Checker", CreateState());

        var restored = RoundTrip(original);

        Assert.Equal(typeof(PulseCheckerState).FullName, restored.StateType);
    }

    // Releases up to 2.3.0 annotated the enums for Newtonsoft, which wrote them as names. That
    // annotation is gone, so documents already in a container must still read back correctly.
    [Fact]
    public void Deserialize_ForEnumsStoredAsNamesByAnEarlierRelease_ReadsThemBack()
    {
        var storedByEarlierRelease = """
            {
              "id": "Some.Checker",
              "StateType": "Healthie.Abstractions.Models.PulseCheckerState",
              "Value": {
                "LastExecutionDateTime": "2026-07-15T12:00:00Z",
                "LastResult": { "Health": "Suspicious", "Message": "flaky" },
                "Interval": "Every30Seconds",
                "ConsecutiveFailureCount": 1,
                "UnhealthyThreshold": 2,
                "IsActive": true,
                "IsHistoryEnabled": true,
                "History": [ { "Health": "Unhealthy", "Message": "boom", "ExecutedAt": "2026-07-15T12:00:00Z" } ]
              }
            }
            """;

        var restored = JsonConvert.DeserializeObject<StateDocument<PulseCheckerState>>(storedByEarlierRelease)!;

        Assert.Equal(PulseCheckerHealth.Suspicious, restored.Value!.LastResult?.Health);
        Assert.Equal(PulseInterval.Every30Seconds, restored.Value.Interval);
        Assert.Equal(PulseCheckerHealth.Unhealthy, Assert.Single(restored.Value.History).Health);
    }

    // CosmosDB requires the identifier to be serialized as lowercase "id".
    [Fact]
    public void Serialize_WritesTheIdentifierAsLowercaseId()
    {
        var json = JsonConvert.SerializeObject(new StateDocument<PulseCheckerState>("Some.Checker", CreateState()));

        Assert.Contains("\"id\":\"Some.Checker\"", json, StringComparison.Ordinal);
    }
}
