using Healthie.PulseChecking.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Scheduling;

public interface IPulsesScheduler : IHostedService
{
    public Dictionary<string, State> GetPulsesStates();
}
