using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.Scheduling;
using Healthie.Abstractions.StateProviding;
using Healthie.DependencyInjection;

namespace Healthie.Tests.Unit;

/// <summary>
/// A pulse checker that always reports healthy. Discovered by assembly scanning in
/// registration tests.
/// </summary>
internal sealed class AlwaysHealthyPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.EveryMinute)
{
    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));
}

/// <summary>
/// A pulse checker that always reports unhealthy. Discovered by assembly scanning in
/// registration tests.
/// </summary>
internal sealed class AlwaysUnhealthyPulseChecker(IStateProvider stateProvider)
    : PulseChecker(stateProvider, PulseInterval.EveryMinute)
{
    public override Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Unhealthy, "down"));
}

/// <summary>
/// An <see cref="IPulseChecker"/> that implements the interface directly rather than deriving from
/// <see cref="PulseChecker"/>. Owning the event outright is what lets a test see whether a
/// subscriber is still attached, and skipping the base class is what lets
/// <see cref="TriggerAsync"/> genuinely throw -- <see cref="PulseChecker.TriggerAsync"/> turns a
/// failing check into an unhealthy result instead of letting it propagate.
/// </summary>
internal sealed class FakePulseChecker(string name) : IPulseChecker
{
    private PulseCheckerState _state = new(PulseInterval.EveryMinute, 0);

    public event EventHandler<PulseCheckerStateChangedEventArgs>? StateChanged;

    /// <summary>How many handlers are attached to <see cref="StateChanged"/>.</summary>
    public int SubscriberCount => StateChanged?.GetInvocationList().Length ?? 0;

    /// <summary>Thrown by <see cref="TriggerAsync"/> when set.</summary>
    public Exception? ThrowOnTrigger { get; set; }

    public int TriggerCount { get; private set; }

    public string Name => name;

    public string DisplayName => name;

    /// <summary>Raises <see cref="StateChanged"/> as a real checker would after a state change.</summary>
    public void RaiseStateChanged(PulseCheckerHealth health)
    {
        var oldState = _state;
        _state = new PulseCheckerState(PulseInterval.EveryMinute, 0)
        {
            LastResult = new PulseCheckerResult(health, health.ToString()),
        };
        StateChanged?.Invoke(this, new PulseCheckerStateChangedEventArgs(oldState, _state));
    }

    public Task TriggerAsync(CancellationToken cancellationToken = default)
    {
        TriggerCount++;
        return ThrowOnTrigger is not null ? Task.FromException(ThrowOnTrigger) : Task.CompletedTask;
    }

    public Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PulseCheckerResult(PulseCheckerHealth.Healthy, "ok"));

    public Task<PulseCheckerState> GetStateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_state);

    public Task SetStateAsync(PulseCheckerState state, CancellationToken cancellationToken = default)
    {
        _state = state;
        return Task.CompletedTask;
    }

    public Task SetIntervalAsync(PulseInterval interval, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetUnhealthyThresholdAsync(uint threshold, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> StopAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> StartAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<List<PulseCheckerHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_state.History);

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetHistoryEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>A state provider stand-in used to assert registration precedence.</summary>
internal sealed class CustomStateProvider : IStateProvider
{
    public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
        => Task.FromResult<TState?>(default);

    public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// A state provider whose reads and writes can be made to fail, standing in for storage being
/// unreachable.
/// </summary>
internal sealed class FaultyStateProvider : IStateProvider
{
    private readonly InMemoryStateProvider _inner = new();

    private int _writeFailuresRemaining;

    /// <summary>Makes the next <paramref name="count"/> writes fail, then recover.</summary>
    public void FailNextWrites(int count) => _writeFailuresRemaining = count;

    public Task<TState?> GetStateAsync<TState>(string name, CancellationToken cancellationToken = default)
        => _inner.GetStateAsync<TState>(name, cancellationToken);

    public Task SetStateAsync<TState>(string name, TState state, CancellationToken cancellationToken = default)
    {
        if (_writeFailuresRemaining > 0)
        {
            _writeFailuresRemaining--;
            throw new InvalidOperationException("the state store is unreachable");
        }

        return _inner.SetStateAsync(name, state, cancellationToken);
    }
}

/// <summary>A scheduler stand-in used to assert registration precedence.</summary>
internal sealed class CustomPulseScheduler : IPulseScheduler
{
    public Task ScheduleAsync(IPulseChecker checker, PulseInterval interval, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UnscheduleAsync(IPulseChecker checker, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
