namespace Healthie.Dashboard;

/// <summary>
/// Configuration options for the Healthie.NET UI dashboard.
/// </summary>
public class HealthieUIOptions
{
    /// <summary>
    /// Gets or sets the title displayed at the top of the dashboard.
    /// </summary>
    /// <remarks>Defaults to <c>"System Health"</c>.</remarks>
    public string DashboardTitle { get; set; } = "System Health";

    /// <summary>
    /// Gets or sets a value indicating whether the dark/light mode toggle is visible.
    /// </summary>
    /// <remarks>Defaults to <c>true</c>.</remarks>
    /// TODO: Will be implemented in a future release. Currently always returns <c>false</c>.
    public bool EnableDarkModeToggle => false;
}
