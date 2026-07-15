namespace Healthie.Mcp;

/// <summary>
/// Configuration options for the Healthie.NET MCP server.
/// </summary>
public class HealthieMcpOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether tools that change a checker's state are exposed.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c>, leaving the server read-only. Enabling it lets a model run a check
    /// on demand and reset a checker's state. Combine it with authorization on the endpoint, since
    /// anything that can reach the endpoint can then trigger checks against your infrastructure.
    /// </remarks>
    public bool AllowMutations { get; set; }

    /// <summary>
    /// Gets or sets the largest number of history entries a single call may return.
    /// </summary>
    /// <remarks>Defaults to 50. Values below 1 are treated as 1.</remarks>
    public int MaxHistoryPageSize { get; set; } = 50;
}
