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
    public bool EnableDarkModeToggle { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the controls that change a checker are rendered.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>: running, pausing, resetting, retiming, and tagging checkers is what
    /// the dashboard is for. Setting it to <c>false</c> leaves a board that only reports -- every
    /// state, sparkline, and event stays visible, and nothing on it can be changed -- which is what
    /// makes it safe to show to an audience wider than the people trusted to stop a production
    /// check.
    /// <para>
    /// This decides what the dashboard renders. It is not authorization: every viewer gets the same
    /// board, so it cannot hand one person the controls and deny another. Gate the endpoint with
    /// <c>RequireAuthorization</c> when who is looking is the question.
    /// </para>
    /// </remarks>
    public bool AllowMutations { get; set; } = true;
}
