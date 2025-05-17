using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IState
{
    PulseCheckerState GetState();
    void SetState(PulseCheckerState state);
}
