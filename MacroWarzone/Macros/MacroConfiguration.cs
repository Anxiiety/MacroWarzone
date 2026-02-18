using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;

namespace MacroWarzone;

/// <summary>
/// Configurazione delle macro (versione SEMPLIFICATA senza weapon switching).
/// </summary>
public sealed class MacroConfiguration
{
    public AntiRecoilConfig AntiRecoil { get; set; } = new();
    public AimAssistConfig AimAssist { get; set; } = new();

    public ZenCronusAimAssistConfig ZenCronusAimAssist { get; set; } = new();
    public AutoPingConfig AutoPing { get; set; } = new();
    public IsUsingSniperConfig IsUsingSniper { get; set; } = new();
    public RapidFireConfig RapidFire { get; set; } = new();
}

// ============================================================================
// ANTI-RECOIL (SINGLE PROFILE, nessun weapon switching)
// ============================================================================
public sealed class AntiRecoilConfig
{
    public bool Enabled { get; set; } = true;

    // Trigger
    public string Trigger { get; set; } = "R1";

    // ✅ SINGLE WEAPON CONFIG (no array, no switching)
    public double RecoilStrength { get; set; } = 0.24;
    public double HorizontalBias { get; set; } = 0.0;
    public double VerticalBias { get; set; } = -1.0;

    // Parametri temporali
    public double SmoothingTauMs { get; set; } = 25;
    public int RampUpMs { get; set; } = 120;
    public int RampDownMs { get; set; } = 80;
}

// ============================================================================
// AIM ASSIST
// ============================================================================
public sealed class AimAssistConfig
{
    public bool Enabled { get; set; } = true;

    // Trigger (combo)
    public string ActivationTrigger { get; set; } = "L1+R1";
    public string ADSTrigger { get; set; } = "L2";

    // Rotazione
    public double RotationStrength { get; set; } = 0.20;
    public double RotationConeAngle { get; set; } = 35.0;
    public double StrafeThreshold { get; set; } = 0.05;
    public double CameraThreshold { get; set; } = 0.08;
    public double TargetMotionThreshold { get; set; } = 0.05;

    // Slowdown
    public double SlowdownRadius { get; set; } = 0.25;
    public double SlowdownStrength { get; set; } = 0.65;
    public double SlowdownSmoothMs { get; set; } = 40;

    // Drag
    public double DragStrength { get; set; } = 0.30;
    public double DragRadius { get; set; } = 0.20;
    public double DragSmoothMs { get; set; } = 35;

    // ADS multipliers
    public double AdsRotationMult { get; set; } = 0.7;
    public double AdsDragMult { get; set; } = 1.6;
}
// ============================================================================
// ✅ CRONUS ZEN AIM ASSIST (NEW)
// ============================================================================
public sealed class ZenCronusAimAssistConfig
{
    public bool Enabled { get; set; } = false;

    // Trigger
    public string ActivationTrigger { get; set; } = "L1+R1";
    public string ADSTrigger { get; set; } = "L2";

    // === STICKY BUBBLE (Slowdown Zone) ===
    public double BubbleRadius { get; set; } = 0.20;   // 20% schermo
    public double BubbleStrength { get; set; } = 0.85;   // 85% slowdown
    public double BubbleSmoothMs { get; set; } = 25;

    // === MICRO-CORRECTION (Pull magnetico) ===
    public double MicroCorrectionRadius { get; set; } = 0.10;   // 10% schermo
    public double MicroCorrectionStrength { get; set; } = 0.20;   // 20% pull
    public double MicroCorrectionSmoothMs { get; set; } = 20;

    // === SHAKE DAMPENING (Anti-jitter) ===
    public double ShakeThreshold { get; set; } = 3.5;    // rad/s
    public double ShakeDampening { get; set; } = 0.70;   // 70% soppress

    // === ADS BOOST ===
    public double AdsMultiplier { get; set; } = 1.5;     // +50% in ADS

    // === RESPONSE CURVE OVERRIDE ===
    public bool UseResponseOverride { get; set; } = true;
    public double ResponseCenterBoost { get; set; } = 1.3;  // +30% sens centro
}


// ============================================================================
// AUTO PING
// ============================================================================
public sealed class AutoPingConfig
{
    public bool Enabled { get; set; } = false;

    public string Trigger { get; set; } = "L1+R1";
    public int PingDurationMs { get; set; } = 100;
}

// ============================================================================
// IS USING SNIPER
// ============================================================================
public sealed class IsUsingSniperConfig
{
    public bool Enabled { get; set; } = false;

    public string Trigger { get; set; } = "L1";
}

// ============================================================================
// RAPID FIRE
// ============================================================================
public sealed class RapidFireConfig
{
    public bool Enabled { get; set; } = false;

    public string Trigger { get; set; } = "R1";
    public int FireRateHz { get; set; } = 15;
}

// ============================================================================
// TRIGGER PARSER (da stringa → Func<Snapshot, bool>)
// ============================================================================
public static class MacroTriggerParser
{
    public static Func<RawInputState.Snapshot, bool> ParseTrigger(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger)) return _ => false;

        // Supporta OR con "|"
        if (trigger.Contains('|'))
        {
            var orParts = trigger.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var orPredicates = new List<Func<RawInputState.Snapshot, bool>>();

            foreach (var orPart in orParts)
            {
                orPredicates.Add(ParseTriggerAnd(orPart));
            }

            return s => orPredicates.Any(p => p(s));
        }

        return ParseTriggerAnd(trigger);
    }

    private static Func<RawInputState.Snapshot, bool> ParseTriggerAnd(string trigger)
    {
        var parts = trigger.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var predicates = new List<Func<RawInputState.Snapshot, bool>>();

        foreach (var part in parts)
        {
            predicates.Add(GetSinglePredicate(part));
        }

        return predicates.Count switch
        {
            0 => _ => false,
            1 => predicates[0],
            _ => s => predicates.TrueForAll(p => p(s))
        };
    }

    private static Func<RawInputState.Snapshot, bool> GetSinglePredicate(string button)
    {
        return button.ToUpperInvariant() switch
        {
            "L1" => s => s.L1,
            "R1" => s => s.R1,
            "L2" => s => s.L2 > 20,
            "R2" => s => s.R2 > 20,
            "L3" => s => s.L3,
            "R3" => s => s.R3,
            "TRIANGLE" => s => s.Triangle,
            "SQUARE" => s => s.Square,
            "CROSS" => s => s.Cross,
            "CIRCLE" => s => s.Circle,
            "DPAD_UP" or "DUP" => s => s.DUp,
            "DPAD_DOWN" or "DDOWN" => s => s.DDown,
            "DPAD_LEFT" or "DLEFT" => s => s.DLeft,
            "DPAD_RIGHT" or "DRIGHT" => s => s.DRight,
            "OPTIONS" => s => s.Options,
            "SHARE" => s => s.Share,
            _ => _ => false
        };
    }
}