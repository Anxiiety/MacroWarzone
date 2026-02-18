using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System;

namespace MacroWarzone;

/// <summary>
/// Output driver per emulazione DualShock4 tramite ViGEm.
///
/// NOTA IMPORTANTE VERSIONE LIBRERIA:
/// - Nefarius.ViGEm.Client 1.21.x
/// - DualShock4: SetAxisValue accetta BYTE (0-255)
///
/// Per DualShock4 dobbiamo convertire:
/// - Normalizzato [-1.0, +1.0] → byte [0, 255] (centro a 128)
/// </summary>
public sealed class ViGEmOutput : IGamepadOutput
{
    private readonly ViGEmClient _client = new();
    private readonly IDualShock4Controller _pad;

    public ViGEmOutput()
    {
        _pad = _client.CreateDualShock4Controller();
    }

    public void Connect()
    {
        _pad.Connect();
    }

    public void Send(in OutputState o)
    {
        // === STICK ANALOGICI ===
        // DS4 in ViGEm 1.21.x usa byte (0-255, centro a 128).
        // Conversione: [-1.0, +1.0] -> [0, 255], con 0.0 -> 128.
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, NormalizedToByte(o.LeftX));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, NormalizedToByte(o.LeftY));
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, NormalizedToByte(o.RightX));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, NormalizedToByte(o.RightY));

        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, o.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, o.R2);

        _pad.SetButtonState(DualShock4Button.ShoulderLeft, o.L1);
        _pad.SetButtonState(DualShock4Button.ShoulderRight, o.R1);

        _pad.SetButtonState(DualShock4Button.Triangle, o.Triangle);
        _pad.SetButtonState(DualShock4Button.Square, o.Square);
        _pad.SetButtonState(DualShock4Button.Cross, o.Cross);
        _pad.SetButtonState(DualShock4Button.Circle, o.Circle);

        _pad.SetDPadDirection(CalculateDPad(o.DUp, o.DDown, o.DLeft, o.DRight));

        _pad.SetButtonState(DualShock4Button.Options, o.Options);

        // Alias legacy: Share e TouchClick sono equivalenti lato output.
        bool shouldPressShare = o.Share || o.TouchClick;
        _pad.SetButtonState(DualShock4Button.Share, shouldPressShare);

        _pad.SetButtonState(DualShock4Button.ThumbLeft, o.L3);
        _pad.SetButtonState(DualShock4Button.ThumbRight, o.R3);
    }

    private static byte NormalizedToByte(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);
        double scaled = (normalized + 1.0) * 127.5;
        return (byte)Math.Clamp((int)Math.Round(scaled), 0, 255);
    }

    private static DualShock4DPadDirection CalculateDPad(bool up, bool down, bool left, bool right)
    {
        if (up && down) { up = false; down = false; }
        if (left && right) { left = false; right = false; }

        if (up && right) return DualShock4DPadDirection.Northeast;
        if (up && left) return DualShock4DPadDirection.Northwest;
        if (down && right) return DualShock4DPadDirection.Southeast;
        if (down && left) return DualShock4DPadDirection.Southwest;

        if (up) return DualShock4DPadDirection.North;
        if (down) return DualShock4DPadDirection.South;
        if (left) return DualShock4DPadDirection.West;
        if (right) return DualShock4DPadDirection.East;

        return DualShock4DPadDirection.None;
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); }
        catch { }
        finally { _client.Dispose(); }
    }
}
