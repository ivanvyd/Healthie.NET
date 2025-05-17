using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;
using Microsoft.Extensions.Hosting;

namespace Healthie.Abstractions.Scheduling;

public interface IPulsesScheduler : IHostedService
{
    Dictionary<string, PulseCheckerState> GetPulsesStates();

    Dictionary<string, IPulseChecker> GetPulseCheckers();

    void SetInterval(string name, PulseInterval interval);

    void Activate(string name);

    void Deactivate(string name);
}
