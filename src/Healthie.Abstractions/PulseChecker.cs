using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Abstractions;

/// <summary>
/// Base class for implementing synchronous pulse checkers.
/// </summary>
public abstract class PulseChecker : IPulseChecker
{
    private readonly IStateProvider _stateProvider;

    private bool _isRunning = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage the state of the pulse checker.</param>
    protected PulseChecker(IStateProvider stateProvider)
    {
        _stateProvider = stateProvider;
    }

    /// <summary>
    /// Gets or sets the state of the pulse checker.
    /// </summary>
    protected PulseCheckerState State
    {
        get => _stateProvider.GetState<PulseCheckerState>(Name) ?? new();
        set => _stateProvider.SetState(Name, value);
    }

    /// <summary>
    /// Performs the pulse check and returns the result.
    /// </summary>
    /// <returns>The result of the pulse check.</returns>
    public abstract PulseCheckerResult Check();

    /// <summary>
    /// Gets the name of the pulse checker.
    /// </summary>
    public string Name => GetType().FullName!;

    /// <summary>
    /// Sets the state of the pulse checker.
    /// </summary>
    /// <param name="state">The state to set.</param>
    public void SetState(PulseCheckerState state)
    {
        State = state;
    }

    /// <summary>
    /// Gets the current state of the pulse checker.
    /// </summary>
    /// <returns>The current state of the pulse checker.</returns>
    public PulseCheckerState GetState()
    {
        return State;
    }

    /// <summary>
    /// Sets the interval at which the pulse check should be performed.
    /// </summary>
    /// <param name="interval">The interval to set.</param>
    public void SetInterval(PulseInterval interval)
    {
        PulseCheckerState state = GetState();
        if (state.Interval == interval)
            return;
        state.Interval = interval;
        SetState(state);
    }

    /// <summary>
    /// Triggers the pulse check.
    /// </summary>
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

    /// <summary>
    /// Stops the pulse checker.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was successfully stopped; otherwise, <c>false</c>.</returns>
    public bool Stop()
    {
        PulseCheckerState state = GetState();
        if (!state.IsActive)
            return false;
        state.IsActive = false;
        SetState(state);
        return true;
    }

    /// <summary>
    /// Starts the pulse checker.
    /// </summary>
    /// <returns><c>true</c> if the pulse checker was successfully started; otherwise, <c>false</c>.</returns>
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
