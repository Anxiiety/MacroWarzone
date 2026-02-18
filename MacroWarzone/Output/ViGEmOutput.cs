using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System;

namespace MacroWarzone;

/// <summary>
/// Output driver per emulazione DualShock4 tramite ViGEm.
/// 
/// NOTA IMPORTANTE VERSIONE LIBRERIA:
/// - Nefarius.ViGEm.Client 1.21.x
/// - DualShock4: SetAxisValue accetta BYTE (0-255)
/// - Xbox360: SetAxisValue accetta SHORT (-32768 a +32767)
/// 
/// Per DualShock4 dobbiamo convertire:
/// - Normalizzato [-1.0, +1.0] → byte [0, 255] (centro a 128)
/// </summary>
public sealed class ViGEmOutput : IDisposable
{
    private readonly ViGEmClient _client = new();
    private readonly IDualShock4Controller _pad;
    private readonly IXbox360Controller _pad2;


    public ViGEmOutput()
    {
        //_pad = _client.CreateDualShock4Controller();
        _pad2 = _client.CreateXbox360Controller();

    }

    public void Connect() 
    {
        //_pad.Connect();
        _pad2.Connect();
    }



    public void Send(in OutputState o)
    {
        // === STICK ANALOGICI ===
        // DualShock4 in ViGEm 1.21.x usa byte (0-255, centro a 128)
        // Dobbiamo convertire da [-1.0, +1.0] a [0, 255]
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, NormalizedToByte(o.LeftX));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, NormalizedToByte(o.LeftY));
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, NormalizedToByte(o.RightX));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, NormalizedToByte(o.RightY));

        // === TRIGGER (L2/R2) ===
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, o.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, o.R2);

        // === BUMPER (L1/R1) ===
        _pad.SetButtonState(DualShock4Button.ShoulderLeft, o.L1);
        _pad.SetButtonState(DualShock4Button.ShoulderRight, o.R1);

        // === PULSANTI FRONTALI ===
        _pad.SetButtonState(DualShock4Button.Triangle, o.Triangle);
        _pad.SetButtonState(DualShock4Button.Square, o.Square);
        _pad.SetButtonState(DualShock4Button.Cross, o.Cross);
        _pad.SetButtonState(DualShock4Button.Circle, o.Circle);

        // === D-PAD ===
        _pad.SetDPadDirection(CalculateDPad(o.DUp, o.DDown, o.DLeft, o.DRight));

        // === OPTIONS / SHARE ===
        _pad.SetButtonState(DualShock4Button.Options, o.Options);

        bool shouldPressShare = o.Share || o.TouchClick;
        _pad.SetButtonState(DualShock4Button.Share, shouldPressShare);

        // === L3 / R3 ===
        _pad.SetButtonState(DualShock4Button.ThumbLeft, o.L3);
        _pad.SetButtonState(DualShock4Button.ThumbRight, o.R3);
    }

    /// <summary>
    /// Converte valore normalizzato [-1.0, +1.0] in byte [0, 255].
    /// Centro (0.0) → 128
    /// </summary>
    private static byte NormalizedToByte(double normalized)
    {
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        // Scala da [-1.0, +1.0] a [0, 255]
        // -1.0 → 0, 0.0 → 128, +1.0 → 255
        double scaled = (normalized + 1.0) * 127.5;

        return (byte)Math.Clamp((int)Math.Round(scaled), 0, 255);
    }

    private static DualShock4DPadDirection CalculateDPad(bool up, bool down, bool left, bool right)
    {
        // Annulla direzioni opposte
        if (up && down) { up = false; down = false; }
        if (left && right) { left = false; right = false; }

        // Diagonali
        if (up && right) return DualShock4DPadDirection.Northeast;
        if (up && left) return DualShock4DPadDirection.Northwest;
        if (down && right) return DualShock4DPadDirection.Southeast;
        if (down && left) return DualShock4DPadDirection.Southwest;

        // Cardinali
        if (up) return DualShock4DPadDirection.North;
        if (down) return DualShock4DPadDirection.South;
        if (left) return DualShock4DPadDirection.West;
        if (right) return DualShock4DPadDirection.East;

        return DualShock4DPadDirection.None;
    }

    public void Dispose()
    {
        try { _pad?.Disconnect(); }
        catch { }
        finally { _client?.Dispose(); }
    }
}