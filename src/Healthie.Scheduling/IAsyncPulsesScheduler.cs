using Healthie.PulseChecking.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Scheduling;

public interface IAsyncPulsesScheduler : IHostedService
{
    Task<Dictionary<string, State>> GetPulsesStatesAsync();
}
