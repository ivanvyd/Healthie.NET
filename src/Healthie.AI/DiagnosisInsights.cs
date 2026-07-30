using Healthie.Abstractions.Insights;

namespace Healthie.AI;

/// <summary>
/// Lets the dashboard ask for an explanation of why a checker has been failing.
/// </summary>
/// <remarks>
/// A string rather than the full <see cref="PulseDiagnosis"/>, because the board shows prose and
/// this keeps the shared contract free of this package's own types. Asked for on demand: the call
/// goes to a language model, so nothing should make it on a board that redraws every second.
/// </remarks>
/// <param name="diagnostician">The diagnostician that talks to the model.</param>
internal sealed class DiagnosisInsights(IPulseDiagnostician diagnostician) : IDiagnosisInsights
{
    /// <inheritdoc />
    public async Task<string> ExplainAsync(string checkerName, CancellationToken cancellationToken = default)
    {
        var diagnosis = await diagnostician.DiagnoseAsync(checkerName, cancellationToken).ConfigureAwait(false);

        return diagnosis.Summary;
    }
}
