using Healthie.PulseChecking.Models;

namespace Healthie.PulseChecking;

public abstract class PulseChecker : IPulseChecker
{
    private State? _state;
    protected State State
    {
        get => _state ?? new();
        set => _state = value;
    }

    public abstract Pulse<Result> Check();

    public string Name => GetType().FullName!;

    public void SetState(State state)
    {
        State = state;
    }

    State IState.GetState()
    {
        return State;
    }

    public void Trigger()
    {
        var currentDateTimeUtc = DateTime.UtcNow;

        var pulseResult = Check();

        SetState(new()
        {
            LastExecutionDateTime = currentDateTimeUtc,
            LastPulse = pulseResult,
        });
    }
}
