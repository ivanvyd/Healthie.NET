using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

public abstract class PulseChecker(IStateProvider stateProvider) : IPulseChecker
{
    private readonly IStateProvider _stateProvider = stateProvider;

    protected State State
    {
        get => _stateProvider.GetState<State>(Name) ?? new();
        set => _stateProvider.SetState(Name, value);
    }

    public abstract Pulse<Result> Check();

    public string Name => GetType().FullName!;

    public void SetState(State state)
    {
        State = state;
    }

    public State GetState()
    {
        return State;
    }

    public void SetInterval(PulseInterval interval)
    {
        State state = GetState();
        state.Interval = interval;
        SetState(state);
    }

    public void Trigger()
    {
        var currentDateTimeUtc = DateTime.UtcNow;

        var pulseResult = Check();

        State state = GetState();
        state.LastExecutionDateTime = currentDateTimeUtc;
        state.LastPulse = pulseResult;

        SetState(state);
    }
}
