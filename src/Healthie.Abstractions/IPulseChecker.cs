using Healthie.Abstractions.Enums;
using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IPulseChecker : IPulse, IState
{
    string Name { get; }
    PulseCheckerResult Check();
    void SetInterval(PulseInterval interval);
    bool Stop();
    bool Start();
}