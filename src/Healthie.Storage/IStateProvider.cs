namespace Healthie.Storage;

public interface IStateProvider
{
    TState? GetState<TState>(string name);

    void SetState<TState>(string name, TState state);
}
