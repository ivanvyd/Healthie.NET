namespace Healthie.Abstractions.StateProviding;

public interface IAsyncStateProviderInitializer
{
    Task InitializeAsync();
}
