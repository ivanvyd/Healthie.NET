using Healthie.PulseChecking.Models;
using Healthie.StateProviding;

namespace Healthie.PulseChecking;

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
