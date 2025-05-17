using Healthie.Abstractions.Models;

namespace Healthie.Abstractions;

public interface IState
{
    State GetState();
    void SetState(State state);
}
