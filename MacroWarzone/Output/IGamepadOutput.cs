namespace MacroWarzone;

public interface IGamepadOutput : IDisposable
{
    void Connect();
    void Send(in OutputState outputState);
}
