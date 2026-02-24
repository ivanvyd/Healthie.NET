using Healthie.Abstractions;
using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Healthie.Abstractions.StateProviding;

namespace Healthie.Sample.BlazorUI.Pulses;

public class MessageQueuePulseChecker : PulseChecker
{
    private readonly Random _random = new();

    public MessageQueuePulseChecker(IStateProvider stateProvider)
        : base(stateProvider, PulseInterval.Every15Seconds, 3)
    {
    }

    public override string DisplayName => "RabbitMQ Broker";

    public override async Task<PulseCheckerResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(20, 150), cancellationToken);

        var queueDepth = _random.Next(0, 50000);
        var consumers = _random.Next(0, 8);

        if (queueDepth < 5000 && consumers >= 2)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Healthy,
                $"Broker online. Queue depth: {queueDepth:N0}, Active consumers: {consumers}, Publish rate: {_random.Next(50, 300)}/s.");
        }

        if (queueDepth < 25000 || consumers >= 1)
        {
            return new PulseCheckerResult(
                PulseCheckerHealth.Suspicious,
                $"Queue backlog growing. Depth: {queueDepth:N0}, Consumers: {consumers}. Messages may be delayed.");
        }

        return new PulseCheckerResult(
            PulseCheckerHealth.Unhealthy,
            $"Queue overflow: {queueDepth:N0} messages pending. No active consumers. Dead letter queue filling up.");
    }
}
