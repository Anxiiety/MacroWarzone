using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;

namespace MacroWarzone;

/// <summary>
/// Output driver per emulazione Xbox 360 tramite ViGEm.
///
/// NOTA IMPORTANTE VERSIONE LIBRERIA:
/// - Nefarius.ViGEm.Client 1.21.x
/// - Xbox360: SetAxisValue accetta SHORT (-32768..+32767)
///
/// Conversione stick:
/// - Normalizzato [-1.0, +1.0] -> short [-32768, +32767]
/// - +1.0 va clampato a +32767 per evitare overflow.
/// </summary>
public sealed class ViGEmX360Output : IGamepadOutput
{
    private readonly ViGEmClient _client = new();
    private readonly IXbox360Controller _pad;

    public ViGEmX360Output()
    {
        _pad = _client.CreateXbox360Controller();
    }

    public void Connect()
    {
        _pad.Connect();
    }

    public void Send(in OutputState o)
    {
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, NormalizedToShort(o.LeftX));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, NormalizedToShort(o.LeftY));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, NormalizedToShort(o.RightX));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, NormalizedToShort(o.RightY));

        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, o.L2);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, o.R2);

        _pad.SetButtonState(Xbox360Button.LeftShoulder, o.L1);
        _pad.SetButtonState(Xbox360Button.RightShoulder, o.R1);

        _pad.SetButtonState(Xbox360Button.Y, o.Triangle);
        _pad.SetButtonState(Xbox360Button.X, o.Square);
        _pad.SetButtonState(Xbox360Button.A, o.Cross);
        _pad.SetButtonState(Xbox360Button.B, o.Circle);

        _pad.SetButtonState(Xbox360Button.Up, o.DUp);
        _pad.SetButtonState(Xbox360Button.Down, o.DDown);
        _pad.SetButtonState(Xbox360Button.Left, o.DLeft);
        _pad.SetButtonState(Xbox360Button.Right, o.DRight);

        _pad.SetButtonState(Xbox360Button.Start, o.Options);

        // Regola alias richiesta: Share è equivalente a TouchClick in modalità Xbox.
        // Xbox non ha touchpad: entrambe le azioni confluiscono su Back.
        bool shouldPressBack = o.Share || o.TouchClick;
        _pad.SetButtonState(Xbox360Button.Back, shouldPressBack);

        _pad.SetButtonState(Xbox360Button.LeftThumb, o.L3);
        _pad.SetButtonState(Xbox360Button.RightThumb, o.R3);
    }

    private static short NormalizedToShort(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        if (normalized >= 1.0)
            return short.MaxValue;

        if (normalized <= -1.0)
            return short.MinValue;

        return (short)Math.Round(normalized * short.MaxValue);
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); }
        catch { }
        finally { _client.Dispose(); }
    }
}
