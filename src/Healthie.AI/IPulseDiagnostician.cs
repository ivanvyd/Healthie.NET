namespace Healthie.AI;

/// <summary>
/// Explains what a pulse checker's recent history shows.
/// </summary>
public interface IPulseDiagnostician
{
    /// <summary>
    /// Reads a checker's recent history and explains what it shows.
    /// </summary>
    /// <param name="name">The name of the pulse checker to diagnose.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the diagnosis.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is empty, whitespace, or names no known checker.
    /// </exception>
    Task<PulseDiagnosis> DiagnoseAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>What a checker's recent history was found to show.</summary>
/// <param name="Name">The checker the diagnosis is about.</param>
/// <param name="Summary">A plain-language reading of the checker's recent history.</param>
/// <param name="Anomaly">
/// How the recent failure rate compares with the checker's earlier history. Measured arithmetically
/// rather than by the model, so it is worth trusting on its own.
/// </param>
public record PulseDiagnosis(string Name, string Summary, AnomalyReport Anomaly);
