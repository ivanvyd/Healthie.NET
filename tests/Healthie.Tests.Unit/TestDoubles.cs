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
