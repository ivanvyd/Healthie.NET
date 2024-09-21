namespace Healthie.PulseChecking;

public interface IAsyncPulse
{
    Task TriggerAsync();
}
