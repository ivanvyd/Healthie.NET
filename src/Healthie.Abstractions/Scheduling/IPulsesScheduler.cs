using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public interface IPulsesScheduler : IHostedService
{
    Dictionary<string, State> GetPulsesStates();

    Dictionary<string, IPulseChecker> GetPulseCheckers();

    void SetInterval(string name, PulseInterval interval);
}
