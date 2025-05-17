using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public interface IAsyncPulsesScheduler : IHostedService
{
    Task<Dictionary<string, State>> GetPulsesStatesAsync();
}
