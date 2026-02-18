using System;
using System.Diagnostics;

namespace MacroWarzone.Macros;

/// <summary>
/// CRONUS ZEN AIM ASSIST: Replica algoritmo Zen basato su script GPC pubblici.
/// 
/// COMPONENTI:
///   1. STICKY BUBBLE      - Slowdown magnetico attorno al centro
///   2. MICRO-CORRECTION   - Pull verso centro quando molto vicino
///   3. SHAKE DAMPENING    - Sopprime tremori involontari
///   4. ADS BOOST          - Potenziamento in ADS
///   5. RESPONSE OVERRIDE  - Curva risposta modificata (opzionale)
/// 
/// DESIGN:
///   - NON usa posizione nemico reale (troppo rischioso per anti-cheat)
///   - Simula target fisso al centro schermo
///   - Comportamento identico a Zen/Cronus hardware
/// 
/// PARAMETRI OTTIMALI (da test community):
///   - BubbleRadius: 0.15-0.25 (15-25% dello schermo)
///   - BubbleStrength: 0.70-0.90 (70-90% slowdown)
///   - MicroCorrectionRadius: 0.08-0.12
///   - MicroCorrectionStrength: 0.15-0.30
///   - ADSMultiplier: 1.3-1.7
/// </summary>
public sealed class ZenCronusAimAssistRule : IMacroRule
{
    private readonly Func<RawInputState.Snapshot, bool> _enabled;
    private readonly Func<RawInputState.Snapshot, bool> _isADS;

    // === PARAMETRI BUBBLE (Slowdown) ===
    private readonly double _bubbleRadius;        // Raggio zona slowdown (0.15 = 15% schermo)
    private readonly double _bubbleStrength;      // Forza slowdown (0.85 = riduce sens a 15%)
    private readonly double _bubbleSmoothMs;      // Smoothing transizione (ms)

    // === PARAMETRI MICRO-CORRECTION (Pull) ===
    private readonly double _microCorrectionRadius;    // Raggio zona pull (più piccolo di bubble)
    private readonly double _microCorrectionStrength;  // Forza pull verso centro
    private readonly double _microCorrectionSmoothMs;

    // === PARAMETRI SHAKE DAMPENING ===
    private readonly double _shakeThreshold;       // Velocità angular per rilevare shake (rad/s)
    private readonly double _shakeDampening;       // Quanto sopprimere (0.7 = riduci 70%)

    // === PARAMETRI ADS ===
    private readonly double _adsMultiplier;        // Moltiplicatore in ADS (1.5 = +50% forza)

    // === PARAMETRI RESPONSE CURVE (opzionale) ===
    private readonly bool _useResponseOverride;
    private readonly double _responseCenterBoost;  // Boost sensibilità al centro (1.3 = +30%)

    // === STATO INTERNO ===
    private long _lastTicks;
    private double _prevRightX, _prevRightY;
    private double _currentSlowdown = 1.0;         // Slowdown corrente (smoothed)
    private double _currentCorrectionX, _currentCorrectionY;

    public ZenCronusAimAssistRule(
        Func<RawInputState.Snapshot, bool> enabled,
        Func<RawInputState.Snapshot, bool> isADS,

        // Bubble (sticky aim)
        double bubbleRadius = 0.20,   // 20% schermo (sweet spot)
        double bubbleStrength = 0.85,   // 85% slowdown (molto forte)
        double bubbleSmoothMs = 25,

        // Micro-correction (pull)
        double microCorrectionRadius = 0.10,   // 10% schermo (zona molto vicina)
        double microCorrectionStrength = 0.20, // 20% pull
        double microCorrectionSmoothMs = 20,

        // Shake dampening
        double shakeThreshold = 3.5,           // 3.5 rad/s (abbastanza veloce)
        double shakeDampening = 0.70,          // Sopprimi 70% dello shake

        // ADS boost
        double adsMultiplier = 1.5,            // +50% in ADS

        // Response curve override
        bool useResponseOverride = true,
        double responseCenterBoost = 1.3)      // +30% sens al centro
    {
        _enabled = enabled;
        _isADS = isADS;

        _bubbleRadius = Math.Clamp(bubbleRadius, 0.05, 0.5);
        _bubbleStrength = Math.Clamp(bubbleStrength, 0.0, 0.95);
        _bubbleSmoothMs = Math.Max(5, bubbleSmoothMs);

        _microCorrectionRadius = Math.Clamp(microCorrectionRadius, 0.02, 0.3);
        _microCorrectionStrength = Math.Clamp(microCorrectionStrength, 0.0, 0.5);
        _microCorrectionSmoothMs = Math.Max(5, microCorrectionSmoothMs);

        _shakeThreshold = Math.Clamp(shakeThreshold, 0.5, 10.0);
        _shakeDampening = Math.Clamp(shakeDampening, 0.0, 0.95);

        _adsMultiplier = Math.Clamp(adsMultiplier, 1.0, 3.0);

        _useResponseOverride = useResponseOverride;
        _responseCenterBoost = Math.Clamp(responseCenterBoost, 1.0, 2.0);
    }

    public void Apply(in RawInputState.Snapshot input, ref OutputState output)
    {
        if (!_enabled(input))
        {
            ResetState();
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastTicks == 0)
        {
            _lastTicks = now;
            _prevRightX = output.RightX;
            _prevRightY = output.RightY;
            return;
        }

        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        bool ads = _isADS(input);

        double rx = output.RightX;
        double ry = output.RightY;

        // ===== STEP 0: RESPONSE CURVE OVERRIDE (opzionale) =====
        if (_useResponseOverride)
        {
            (rx, ry) = ApplyResponseCurveOverride(rx, ry);
        }

        // ===== STEP 1: SHAKE DAMPENING =====
        double deltaX = rx - _prevRightX;
        double deltaY = ry - _prevRightY;
        double angularVelocity = Math.Sqrt(deltaX * deltaX + deltaY * deltaY) / Math.Max(dt, 0.001);

        if (angularVelocity > _shakeThreshold)
        {
            // Shake detected → sopprimi parzialmente
            double dampenFactor = 1.0 - _shakeDampening;
            rx = _prevRightX + (deltaX * dampenFactor);
            ry = _prevRightY + (deltaY * dampenFactor);
        }

        // ===== STEP 2: CALCOLA DISTANZA DA CENTRO (target simulato) =====
        // Centro schermo = (0, 0)
        double distanceFromCenter = Math.Sqrt(rx * rx + ry * ry);

        // ===== STEP 3: STICKY BUBBLE (Slowdown) =====
        double targetSlowdown = 1.0;  // Nessun slowdown di default

        if (distanceFromCenter < _bubbleRadius)
        {
            // Dentro la bubble → calcola slowdown
            double bubbleFactor = 1.0 - (distanceFromCenter / _bubbleRadius);  // 1.0 al centro, 0.0 al bordo
            double strength = _bubbleStrength;

            // ADS boost
            if (ads) strength = Math.Min(0.95, strength * _adsMultiplier);

            targetSlowdown = 1.0 - (strength * bubbleFactor);
        }

        // Smooth slowdown transition
        double alphaSlowdown = AlphaFromTauMs(_bubbleSmoothMs, dt);
        _currentSlowdown = Lerp(_currentSlowdown, targetSlowdown, alphaSlowdown);

        // ===== STEP 4: MICRO-CORRECTION (Pull) =====
        double targetCorrectionX = 0.0;
        double targetCorrectionY = 0.0;

        if (distanceFromCenter < _microCorrectionRadius && distanceFromCenter > 0.001)
        {
            // Molto vicino al centro → tira verso centro
            double pullFactor = 1.0 - (distanceFromCenter / _microCorrectionRadius);
            double strength = _microCorrectionStrength;

            // ADS boost
            if (ads) strength = Math.Min(0.5, strength * _adsMultiplier);

            // Calcola vettore correzione (verso centro = opposto alla posizione)
            targetCorrectionX = -rx * strength * pullFactor;
            targetCorrectionY = -ry * strength * pullFactor;
        }

        // Smooth correction
        double alphaCorrection = AlphaFromTauMs(_microCorrectionSmoothMs, dt);
        _currentCorrectionX = Lerp(_currentCorrectionX, targetCorrectionX, alphaCorrection);
        _currentCorrectionY = Lerp(_currentCorrectionY, targetCorrectionY, alphaCorrection);

        // ===== STEP 5: APPLICA TUTTO =====

        // Applica micro-correction
        double newRightX = rx + _currentCorrectionX;
        double newRightY = ry + _currentCorrectionY;

        // Applica slowdown
        newRightX *= _currentSlowdown;
        newRightY *= _currentSlowdown;

        // Clamp finale
        newRightX = Math.Clamp(newRightX, -1.0, 1.0);
        newRightY = Math.Clamp(newRightY, -1.0, 1.0);

        output = output with
        {
            RightX = newRightX,
            RightY = newRightY
        };

        // Salva per prossimo frame
        _prevRightX = rx;  // Salva input originale (prima delle modifiche)
        _prevRightY = ry;
    }

    /// <summary>
    /// Response Curve Override: aumenta sensibilità al centro per controllo fine.
    /// Simula comportamento "expo inverso" di Zen.
    /// </summary>
    private (double x, double y) ApplyResponseCurveOverride(double x, double y)
    {
        double magnitude = Math.Sqrt(x * x + y * y);

        if (magnitude < 0.001) return (x, y);

        // Al centro (magnitude < 0.3) → boost sensibilità
        if (magnitude < 0.3)
        {
            double boost = _responseCenterBoost;
            x *= boost;
            y *= boost;
        }

        return (x, y);
    }

    private void ResetState()
    {
        _lastTicks = 0;
        _currentSlowdown = 1.0;
        _currentCorrectionX = 0.0;
        _currentCorrectionY = 0.0;
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * Math.Clamp(t, 0.0, 1.0);
    }

    private static double AlphaFromTauMs(double tauMs, double dtSec)
    {
        double tau = tauMs / 1000.0;
        if (tau <= 1e-6) return 1.0;
        return 1.0 - Math.Exp(-dtSec / tau);
    }
}