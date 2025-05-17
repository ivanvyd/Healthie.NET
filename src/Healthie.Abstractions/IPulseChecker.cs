using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IPulseChecker : IPulse, IState
{
    string Name { get; }
    Pulse<Result> Check();
}