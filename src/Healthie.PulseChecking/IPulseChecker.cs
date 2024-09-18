using Healthie.PulseChecking.Models;

namespace Healthie.PulseChecking;

public interface IPulseChecker : IPulse, IState
{
    string Name { get; }
    Pulse<Result> Check();
}