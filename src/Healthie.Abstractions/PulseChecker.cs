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
    private readonly PulseInterval _initialInterval;
    private readonly uint _initialUnhealthyThreshold;
    private bool _isRunning = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage the state of the pulse checker.</param>
    protected PulseChecker(IStateProvider stateProvider) 
        : this(stateProvider, PulseInterval.EveryMinute, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with a specific interval.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage the state of the pulse checker.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    protected PulseChecker(IStateProvider stateProvider, PulseInterval initialInterval)
        : this(stateProvider, initialInterval, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseChecker"/> class with a specific interval and unhealthy threshold.
    /// </summary>
    /// <param name="stateProvider">The state provider used to manage the state of the pulse checker.</param>
    /// <param name="initialInterval">The initial interval at which the pulse checker operates.</param>
    /// <param name="unhealthyThreshold">The number of consecutive failures needed to consider the pulse checker unhealthy.</param>
    protected PulseChecker(IStateProvider stateProvider, PulseInterval initialInterval, uint unhealthyThreshold)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _initialInterval = initialInterval;
        _initialUnhealthyThreshold = unhealthyThreshold;
    }

    /// <summary>
    /// Gets or sets the state of the pulse checker.
    /// </summary>
    protected PulseCheckerState State
    {
        get => _stateProvider.GetState<PulseCheckerState>(Name) ?? new(_initialInterval, _initialUnhealthyThreshold);
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
    /// Sets the unhealthy threshold for the pulse checker.
    /// </summary>
    /// <param name="threshold">The threshold to set.</param>
    public void SetUnhealthyThreshold(uint threshold)
    {
        PulseCheckerState state = GetState();
        if (state.UnhealthyThreshold == threshold)
        {
            return;
        }

        state.UnhealthyThreshold = threshold;
        SetState(state);
    }

    /// <summary>
    /// Resets the pulse checker state to healthy.
    /// </summary>
    /// <remarks>
    /// This resets the consecutive failures count, sets the health status to Healthy, and clears any error messages.
    /// </remarks>
    public void Reset()
    {
        PulseCheckerState state = GetState();
        
        // Reset the consecutive failures count
        state.ConsecutiveFailureCount = 0;
        
        // Create a new healthy result
        state.LastResult = new PulseCheckerResult(
            PulseCheckerHealth.Healthy,
            string.Empty);
            
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
            
            // Update consecutive failure count based on the check result
            if (pulseResult.Health == PulseCheckerHealth.Healthy)
            {
                // Reset the failure count on success
                state.ConsecutiveFailureCount = 0;
            }
            else
            {
                // Increment the failure count on failure
                state.ConsecutiveFailureCount++;
                
                // Adjust the pulse result based on failure count if needed
                if (pulseResult.Health != PulseCheckerHealth.Unhealthy && 
                    state.ConsecutiveFailureCount > state.UnhealthyThreshold)
                {
                    // If we've reached the threshold but the result wasn't already unhealthy,
                    // create a new result with the unhealthy status
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Unhealthy, 
                        $"{pulseResult.Message} (Crossed unhealthy threshold: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
                else if (pulseResult.Health == PulseCheckerHealth.Unhealthy && 
                         state.ConsecutiveFailureCount <= state.UnhealthyThreshold)
                {
                    // If result is unhealthy but we haven't crossed the threshold,
                    // downgrade to suspicious
                    pulseResult = new PulseCheckerResult(
                        PulseCheckerHealth.Suspicious, 
                        $"{pulseResult.Message} (Suspicious: {state.ConsecutiveFailureCount}/{state.UnhealthyThreshold})");
                }
            }

            state.LastResult = pulseResult;
            SetState(state);
        }
        catch (Exception ex)
        {
            PulseCheckerState state = GetState();
            state.LastExecutionDateTime = currentDateTimeUtc;
            state.ConsecutiveFailureCount++;
            
            string message = $"{ex.GetType()}: {ex.Message}";
            
            // Determine if this should be unhealthy based on threshold
            var health = state.ConsecutiveFailureCount > state.UnhealthyThreshold 
                ? PulseCheckerHealth.Unhealthy
                : PulseCheckerHealth.Suspicious;
                
            state.LastResult = new(health, message);

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
