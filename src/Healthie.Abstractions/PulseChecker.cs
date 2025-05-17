using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

public abstract class PulseChecker(IStateProvider stateProvider)
    : IPulseChecker
{
    private readonly IStateProvider _stateProvider = stateProvider;

    private bool _isRunning = false;

    protected PulseCheckerState State
    {
        get => _stateProvider.GetState<PulseCheckerState>(Name) ?? new();
        set => _stateProvider.SetState(Name, value);
    }

    public abstract PulseCheckerResult Check();

    public string Name => GetType().FullName!;

    public void SetState(PulseCheckerState state)
    {
        State = state;
    }

    public PulseCheckerState GetState()
    {
        return State;
    }

    public void SetInterval(PulseInterval interval)
    {
        PulseCheckerState state = GetState();
        if (state.Interval == interval)
            return;
        state.Interval = interval;
        SetState(state);
    }

    public void Trigger()
    {
        if (_isRunning)
            return;

        var currentDateTimeUtc = DateTime.UtcNow;

        try
        {
            _isRunning = true;

            var pulseResult = Check();

            PulseCheckerState state = GetState();
            state.LastExecutionDateTime = currentDateTimeUtc;
            state.LastResult = pulseResult;

            SetState(state);
        }
        catch (Exception ex)
        {
            PulseCheckerState state = GetState();
            state.LastExecutionDateTime = currentDateTimeUtc;
            string message = $"{ex.GetType()}: {ex.Message}";
            state.LastResult = new(false, message);

            SetState(state);
        }
        finally
        {
            _isRunning = false;
        }
    }

    public bool Stop()
    {
        PulseCheckerState state = GetState();
        if (!state.IsActive)
            return false;
        state.IsActive = false;
        SetState(state);
        return true;
    }

    public bool Start()
    {
        PulseCheckerState state = GetState();
        if (state.IsActive)
            return false;
        state.IsActive = true;
        SetState(state);
        return true;
    }
}
