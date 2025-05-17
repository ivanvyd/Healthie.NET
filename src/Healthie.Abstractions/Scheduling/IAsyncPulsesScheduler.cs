using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public interface IAsyncPulsesScheduler : IHostedService
{
    Task<Dictionary<string, PulseCheckerState>> GetPulsesStatesAsync();

    Task<Dictionary<string, IAsyncPulseChecker>> GetPulseCheckersAsync();

    Task SetIntervalAsync(string name, PulseInterval interval);

    Task ActivateAsync(string name);

    Task DeactivateAsync(string name);
}
