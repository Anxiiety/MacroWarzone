using System;

namespace MacroWarzone;

/// <summary>
/// Helper matematici per conversione valori controller.
/// 
/// RANGES:
/// - Normalizzato (logica macro): -1.0 a +1.0 (double)
/// - XInput/DualShock4 axis: -32768 a +32767 (short)
/// - Raw byte: 0-255 (centro a 128)
/// - Trigger: 0 a 255 (byte)
/// 
/// DESIGN NOTES:
/// - Xbox360 e DualShock4 usano lo STESSO range per gli stick (-32768 a +32767)
/// - La conversione è identica per entrambi i controller
/// - Clamp aggressivo per evitare overflow
/// </summary>
public static class AxisMath
{
    // Costanti range XInput/DS4
    private const short AxisMin = -32768;
    private const short AxisMax = 32767;

    /// <summary>
    /// Converte valore normalizzato (-1.0 a +1.0) in valore XInput/DualShock4 (-32768 a +32767).
    /// 
    /// USAGE:
    /// - Input da macro/processing: double in [-1.0, +1.0]
    /// - Output per ViGEm: short in [-32768, +32767]
    /// 
    /// THREADING: Thread-safe (pura funzione matematica)
    /// </summary>
    public static short ToXInputAxis(double normalized)
    {
        // Clamp in caso di valori fuori range (safety)
        normalized = Math.Clamp(normalized, -1.0, 1.0);

        // Scala lineare
        double scaled = normalized * AxisMax;

        // Converti a short con clamp finale
        return (short)Math.Clamp((int)Math.Round(scaled), AxisMin, AxisMax);
    }

    /// <summary>
    /// Normalizza valore raw byte (0-255) in range [-1.0, +1.0].
    /// Centro (128) → 0.0
    /// 
    /// USATO DA: OutputLoop per convertire input OSC
    /// </summary>
    public static double NormalizeAxis(byte raw)
    {
        // Sottrai centro (128) per ottenere range [-128, +127]
        int centered = raw - 128;

        // Scala in [-1.0, +1.0]
        // Divisore = 128 per valori negativi, 127 per positivi (asimmetria del byte)
        double divisor = centered < 0 ? 128.0 : 127.0;
        return centered / divisor;
    }

    /// <summary>
    /// Applica deadzone radiale (circolare) agli stick.
    /// 
    /// LOGICA:
    /// - Se magnitudine < deadzone → (0, 0)
    /// - Altrimenti: rimappa linearmente da [deadzone, 1.0] → [0.0, 1.0]
    /// 
    /// USATO DA: StickProcessor per eliminare drift
    /// </summary>
    public static (double x, double y) ApplyRadialDeadzone(double x, double y, double deadzone)
    {
        deadzone = Math.Clamp(deadzone, 0.0, 0.95);

        double magnitude = Math.Sqrt(x * x + y * y);

        if (magnitude < deadzone)
            return (0.0, 0.0);

        // Rimappa da [deadzone, 1.0] a [0.0, 1.0]
        double scale = (magnitude - deadzone) / (1.0 - deadzone);
        scale = Math.Clamp(scale, 0.0, 1.0);

        // Mantieni direzione, scala magnitudine
        double factor = scale / magnitude;
        return (x * factor, y * factor);
    }

    /// <summary>
    /// Applica curva esponenziale (response curve) per sensibilità fine.
    /// 
    /// FORMULA: sign(x) * |x|^(1 + expo)
    /// 
    /// COMPORTAMENTO:
    /// - expo = 0.0 → lineare (nessuna modifica)
    /// - expo > 0.0 → più sensibilità al centro, meno ai bordi
    /// - expo < 0.0 → meno sensibilità al centro, più ai bordi (sconsigliato)
    /// 
    /// USATO DA: StickProcessor per controllo preciso
    /// </summary>
    public static double Expo(double value, double expo)
    {
        expo = Math.Clamp(expo, -0.9, 2.0);

        if (Math.Abs(expo) < 0.001)
            return value; // Lineare

        double sign = Math.Sign(value);
        double abs = Math.Abs(value);

        // Curva esponenziale
        double result = Math.Pow(abs, 1.0 + expo);

        return sign * result;
    }

    /// <summary>
    /// Converte valore raw da controller (0-255) in normalizzato (-1.0 a +1.0).
    /// 
    /// NOTA: Equivalente a NormalizeAxis, mantenuto per compatibilità
    /// </summary>
    public static double FromRawStick(byte raw)
    {
        return NormalizeAxis(raw);
    }

    /// <summary>
    /// Converte valore normalizzato (0.0 a 1.0) in trigger (0-255).
    /// </summary>
    public static byte ToTrigger(double normalized)
    {
        normalized = Math.Clamp(normalized, 0.0, 1.0);
        return (byte)Math.Round(normalized * 255.0);
    }

    /// <summary>
    /// Converte trigger raw (0-255) in normalizzato (0.0 a 1.0).
    /// </summary>
    public static double FromTrigger(byte raw)
    {
        return raw / 255.0;
    }
}