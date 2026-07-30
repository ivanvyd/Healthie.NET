namespace Healthie.Abstractions.Insights;

/// <summary>
/// An explanation of why a checker has been failing.
/// </summary>
/// <remarks>
/// Declared here rather than in the AI package, so the dashboard can offer the button without
/// referencing it -- see <see cref="IUptimeInsights"/> for why that matters.
/// </remarks>
public interface IDiagnosisInsights
{
    /// <summary>
    /// Explains a checker's recent failures.
    /// </summary>
    /// <param name="checkerName">The checker to explain.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The explanation.</returns>
    /// <remarks>
    /// Asked for rather than shown: this goes to a language model, which costs money and takes
    /// seconds, so nothing should call it on a board that redraws every second.
    /// </remarks>
    Task<string> ExplainAsync(string checkerName, CancellationToken cancellationToken = default);
}
