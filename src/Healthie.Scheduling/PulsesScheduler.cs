using Healthie.PulseChecking;
using Microsoft.Extensions.Hosting;

namespace Healthie.Scheduling;

public class PulsesScheduler(IEnumerable<IPulseChecker> pulseCheckers, IPulseScheduler pulseScheduler)
    : BackgroundService
{
    private readonly IEnumerable<IPulseChecker> _pulseCheckers = pulseCheckers ?? throw new ArgumentNullException(nameof(pulseCheckers));
    private readonly IPulseScheduler _pulseScheduler = pulseScheduler ?? throw new ArgumentNullException(nameof(pulseScheduler));

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromMinutes(1);

        foreach (var checker in _pulseCheckers)
        {
            _pulseScheduler.Schedule(checker, interval);
        }

        return Task.CompletedTask;
    }
}
