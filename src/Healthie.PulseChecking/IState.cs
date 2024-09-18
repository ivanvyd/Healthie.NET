using Healthie.PulseChecking.Models;

namespace Healthie.PulseChecking;

public interface IState
{
    State GetState();
    void SetState(State state);
}
