namespace Healthie.Dashboard.Services;

/// <summary>
/// Shared theme state for communicating dark/light mode changes between the
/// <c>HealthieDashboard</c> component and the host application's layout.
/// Registered as a scoped service, so each Blazor circuit gets its own instance.
/// </summary>
/// <remarks>
/// <para>
/// Subscribe to <see cref="OnChanged"/> in your layout to follow the dashboard's theme:
/// </para>
/// <code>
/// @inject HealthieThemeState ThemeState
/// @implements IDisposable
///
/// &lt;div class="@(ThemeState.IsDarkMode ? "my-app-dark" : "my-app-light")"&gt;
///     @Body
/// &lt;/div&gt;
///
/// @code {
///     protected override void OnInitialized() =&gt; ThemeState.OnChanged += StateHasChanged;
///
///     public void Dispose() =&gt; ThemeState.OnChanged -= StateHasChanged;
/// }
/// </code>
/// </remarks>
public sealed class HealthieThemeState
{
    private bool _isDarkMode;

    /// <summary>
    /// Gets or sets whether dark mode is active.
    /// Setting this property raises <see cref="OnChanged"/>.
    /// </summary>
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value) return;
            _isDarkMode = value;
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// Event raised when <see cref="IsDarkMode"/> changes.
    /// </summary>
    public event Action? OnChanged;
}
