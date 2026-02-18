using System;

namespace MacroWarzone;

public static class GamepadOutputFactory
{
    public static IGamepadOutput Create(GamepadOutputType outputType) => outputType switch
    {
        GamepadOutputType.DualShock4 => new ViGEmOutput(),
        GamepadOutputType.Xbox360 => new ViGEmX360Output(),
        _ => throw new ArgumentOutOfRangeException(nameof(outputType), outputType, null)
    };
}
