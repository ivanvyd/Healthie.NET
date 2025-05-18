namespace Healthie.Abstractions.StateProviding;

/// <summary>
/// Defines a contract for initializing a state provider.
/// </summary>
public interface IStateProviderInitializer
{
    /// <summary>
    /// Initializes the state provider.
    /// </summary>
    void Initialize();
}
