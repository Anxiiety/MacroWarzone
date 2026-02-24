using System;
using System.Diagnostics;
using System.Drawing;
using MacroWarzone.Vision;

namespace MacroWarzone.Macros;

/// <summary>
/// AI-Powered Aim Assist: usa YOLO per rilevare nemici reali.
/// 
/// WORKFLOW:
/// 1. Cattura screenshot (30Hz)
/// 2. YOLO detect enemies
/// 3. Calcola target più vicino al centro
/// 4. Smooth interpolation verso target
/// 5. SAFE: max rotation speed, jitter, reaction delay
/// </summary>
public sealed class AIVisionAimAssistRule : IMacroRule
{
    private readonly Func<RawInputState.Snapshot, bool> _enabled;
    private readonly Func<RawInputState.Snapshot, bool> _isADS;

    private readonly ScreenCaptureService _capture;
    private readonly OverlayRenderer _overlay;
    private readonly AIVisionService _aiVision;

    // Parametri
    private readonly double _assistStrength;        // 0.0-1.0
    private readonly double _smoothingMs;           // Smoothing interpolazione
    private readonly double _maxRotationSpeed;      // Max °/sec (sicurezza)
    private readonly int _reactionDelayMs;          // Delay umano (50-150ms)

    // State
    private long _lastCaptureTicks;
    private long _lastDetectionTicks;
    private AIVisionService.Detection? _currentTarget;
    private double _targetX;  // Normalized [0,1]
    private double _targetY;
    private double _currentOffsetX;
    private double _currentOffsetY;

    public AIVisionAimAssistRule(
        Func<RawInputState.Snapshot, bool> enabled,
        Func<RawInputState.Snapshot, bool> isADS,
        ScreenCaptureService capture,
        AIVisionService aiVision,
        OverlayRenderer overlay,
        double assistStrength = 0.3,
        double smoothingMs = 50,
        double maxRotationSpeed = 180.0,  // 180°/sec = sicuro
        int reactionDelayMs = 80)
    {
        _enabled = enabled;
        _isADS = isADS;
        _capture = capture;
        _aiVision = aiVision;
        _overlay = overlay;

        _assistStrength = Math.Clamp(assistStrength, 0.0, 1.0);
        _smoothingMs = Math.Max(10, smoothingMs);
        _maxRotationSpeed = Math.Clamp(maxRotationSpeed, 60, 360);
        _reactionDelayMs = Math.Clamp(reactionDelayMs, 0, 300);
    }

    public void Apply(in RawInputState.Snapshot input, ref OutputState output)
    {
        if (!_enabled(input))
        {
            _currentTarget = null;
            _currentOffsetX = 0;
            _currentOffsetY = 0;
            return;
        }

        long now = Stopwatch.GetTimestamp();

        // === CAPTURE + DETECTION (30Hz max) ===
        double captureIntervalMs = 1000.0 / 30.0; // 33ms = 30 FPS

        if (_lastCaptureTicks == 0 ||
            (now - _lastCaptureTicks) / (double)(Stopwatch.Frequency / 1000) >= captureIntervalMs)
        {
            _lastCaptureTicks = now;

            try
            {
                // Cattura frame (auto-dispose con using)
                using (var screenFrame = _capture.CaptureFrame())
                {
                    if (screenFrame != null)
                    {
                        // Detect ALL enemies per overlay
                        var allDetections = _aiVision.DetectEnemies(screenFrame);
                        Debug.WriteLine($"[AI VISION] Frame captured, detected {allDetections.Count} enemies");

                        // Update overlay sempre (anche 0 detections) per sincronizzare tracking/scomparsa
                        if (_overlay != null)
                        {
                            Debug.WriteLine($"[AI VISION] Detected {allDetections.Count} enemies");
                            _overlay.UpdateDetections(allDetections);
                        }
                        else
                        {
                            Debug.WriteLine($"[AI VISION] Detected {allDetections.Count} enemies (no overlay)");
                        }
                        // Get closest target per aim assist
                        if (allDetections.Count > 0)
                        {
                            var closestTarget = allDetections[0]; // Già ordinato per distanza
                            _currentTarget = closestTarget;
                            _lastDetectionTicks = now;

                            // Converti coordinate normalized → screen offset
                            _targetX = closestTarget.X - 0.5; // Centro = 0
                            _targetY = closestTarget.Y - 0.5;
                        }
                        else
                        {
                            _currentTarget = null;
                        }
                    }
                } // frame disposed qui
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AI VISION] Error: {ex.Message}");
            }
        }

        // === SKIP SE NESSUN TARGET ===
        if (!_currentTarget.HasValue)
        {
            return;
        }

        // === REACTION DELAY (safety) ===
        double timeSinceDetection = (now - _lastDetectionTicks) / (double)(Stopwatch.Frequency / 1000);
        if (timeSinceDetection < _reactionDelayMs)
        {
            return; // Delay umano
        }

        // === CALCOLA OFFSET DESIDERATO ===
        double desiredOffsetX = _targetX * _assistStrength;
        double desiredOffsetY = _targetY * _assistStrength;

        // ADS boost
        if (_isADS(input))
        {
            desiredOffsetX *= 1.5;
            desiredOffsetY *= 1.5;
        }

        // === SMOOTH INTERPOLATION ===
        double dt = 0.005; // 200Hz = 5ms
        double alpha = AlphaFromTauMs(_smoothingMs, dt);

        _currentOffsetX = Lerp(_currentOffsetX, desiredOffsetX, alpha);
        _currentOffsetY = Lerp(_currentOffsetY, desiredOffsetY, alpha);

        // === MAX ROTATION SPEED LIMITER (safety) ===
        double maxDelta = (_maxRotationSpeed / 360.0) * dt; // Normalized per frame

        _currentOffsetX = Math.Clamp(_currentOffsetX, -maxDelta, maxDelta);
        _currentOffsetY = Math.Clamp(_currentOffsetY, -maxDelta, maxDelta);

        // === APPLY TO OUTPUT ===
        double stickX = _currentOffsetX * 2.0; // [-1, 1]
        double stickY = _currentOffsetY * 2.0;

        output = output with
        {
            RightX = Math.Clamp(output.RightX + stickX, -1.0, 1.0),
            RightY = Math.Clamp(output.RightY + stickY, -1.0, 1.0)
        };
    }

    private static double Lerp(double a, double b, double t) =>
        a + (b - a) * Math.Clamp(t, 0.0, 1.0);

    private static double AlphaFromTauMs(double tauMs, double dtSec)
    {
        double tau = tauMs / 1000.0;
        return (tau <= 1e-6) ? 1.0 : 1.0 - Math.Exp(-dtSec / tau);
    }
}