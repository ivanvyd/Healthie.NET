namespace Healthie.Abstractions;

public interface IAsyncPulse
{
    Task TriggerAsync();
}
