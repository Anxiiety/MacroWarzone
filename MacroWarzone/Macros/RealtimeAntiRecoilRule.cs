using System;
using System.Diagnostics;
using System.Linq;
using MacroWarzone.Core;

namespace MacroWarzone.Macros;

public sealed class RealtimeAntiRecoilRule : IMacroRule
{
    private struct RecoilSample
    {
        public long TimestampTicks;
        public double VelocityY;
        public double VelocityX;
        public double RawInputY;
        public double RawInputX;
        public bool IsFiring;
    }

    private readonly Func<RawInputState.Snapshot, bool> _fireCondition;
    private readonly double _adaptiveStrength;
    private readonly double _learningRate;
    private readonly int _bufferSize;
    private readonly int _minSamplesForLearning;
    private readonly int _patternLockThreshold;

    private readonly CircularBuffer<RecoilSample> _buffer;
    private long _lastTicks;
    private long _fireStartTicks;
    private double _prevRightY;
    private double _prevRightX;
    private bool _wasFiring;
    private int _bulletCount;

    // ✅ FIX 1: Edge detection vera per bullet counting
    private bool _wasR1Pressed;  // Stato precedente R1

    private double _currentCompensationY;
    private double _currentCompensationX;
    private double _learnedBaseCompY;
    private double _learnedBaseCompX;
    private bool _isPatternLocked;
    private PatternType _detectedPattern;

    private double _averageFireIntervalMs;
    private int _shotCountForFireRate;
    private long _firstShotTicks;

    private enum PatternType
    {
        Unknown,
        Linear,
        Exponential,
        Stepped,
        Random
    }

    public RealtimeAntiRecoilRule(
        Func<RawInputState.Snapshot, bool> fireCondition,
        double adaptiveStrength = 1.0,
        double learningRate = 0.3,
        int bufferSize = 100,
        int minSamplesForLearning = 10,
        int patternLockThreshold = 15)
    {
        _fireCondition = fireCondition ?? throw new ArgumentNullException(nameof(fireCondition));
        _adaptiveStrength = Math.Clamp(adaptiveStrength, 0.5, 1.5);
        _learningRate = Math.Clamp(learningRate, 0.1, 0.5);
        _bufferSize = Math.Clamp(bufferSize, 50, 200);
        _minSamplesForLearning = Math.Max(5, minSamplesForLearning);
        _patternLockThreshold = Math.Max(10, patternLockThreshold);

        _buffer = new CircularBuffer<RecoilSample>(bufferSize);
        _detectedPattern = PatternType.Unknown;
    }

    public void Apply(in RawInputState.Snapshot input, ref OutputState output)
    {
        long now = Stopwatch.GetTimestamp();
        bool isFiring = _fireCondition(input);

        // DEBUG: Trigger check
        if (input.R1 || input.R2 > 50)
        {
            Debug.WriteLine($"[REALTIME DEBUG] R1={input.R1} R2={input.R2} isFiring={isFiring}");
        }

        if (_lastTicks == 0)
        {
            Debug.WriteLine("[REALTIME DEBUG] First frame initialized");
            _lastTicks = now;
            _prevRightY = output.RightY;
            _prevRightX = output.RightX;
            _wasFiring = isFiring;
            _wasR1Pressed = input.R1;  // ✅ Init edge detection
            return;
        }

        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        // === FIRE START DETECTION ===
        if (isFiring && !_wasFiring)
        {
            Debug.WriteLine("[REALTIME DEBUG] ========== FIRE START ==========");
            _fireStartTicks = now;
            _bulletCount = 0;
            _shotCountForFireRate = 0;
            _firstShotTicks = now;
            _wasR1Pressed = input.R1;  // ✅ Reset edge state
        }

        // === FIRE END DETECTION ===
        if (!isFiring && _wasFiring)
        {
            Debug.WriteLine($"[REALTIME DEBUG] ========== FIRE END (total bullets: {_bulletCount}) ==========");
            _bulletCount = 0;
            _currentCompensationY = 0;
            _currentCompensationX = 0;
        }

        // === ✅ FIX 1: BULLET COUNTING CON EDGE DETECTION VERA ===
        if (isFiring)
        {
            // Edge detection: R1 pressed NOW ma non era pressed BEFORE
            bool isR1JustPressed = input.R1 && !_wasR1Pressed;

            if (isR1JustPressed || _bulletCount == 0)
            {
                _bulletCount++;
                _shotCountForFireRate++;

                Debug.WriteLine($"[REALTIME DEBUG] Bullet #{_bulletCount}");

                if (_shotCountForFireRate > 1)
                {
                    double totalTimeMs = (now - _firstShotTicks) / (double)(Stopwatch.Frequency / 1000);
                    _averageFireIntervalMs = totalTimeMs / (_shotCountForFireRate - 1);
                    double rpm = 60000.0 / _averageFireIntervalMs;

                    if (_bulletCount % 5 == 0)
                    {
                        Debug.WriteLine($"[REALTIME DEBUG] Fire Rate: {rpm:F0} RPM (interval: {_averageFireIntervalMs:F1}ms)");
                    }
                }
            }
        }

        // Update edge state
        _wasR1Pressed = input.R1;

        // === SAMPLE RECORDING ===
        double deltaY = output.RightY - _prevRightY;
        double deltaX = output.RightX - _prevRightX;

        // ✅ FIX 2: Calcola velocità ASSOLUTA (non delta diretto)
        double velocityY = (dt > 0.001) ? deltaY / dt : 0.0;
        double velocityX = (dt > 0.001) ? deltaX / dt : 0.0;

        _buffer.Add(new RecoilSample
        {
            TimestampTicks = now,
            VelocityY = velocityY,
            VelocityX = velocityX,
            RawInputY = output.RightY,
            RawInputX = output.RightX,
            IsFiring = isFiring
        });

        if (isFiring && _bulletCount % 5 == 0)
        {
            Debug.WriteLine($"[REALTIME DEBUG] Buffer: {_buffer.Count}/{_bufferSize} samples");
        }

        _prevRightY = output.RightY;
        _prevRightX = output.RightX;
        _wasFiring = isFiring;

        if (!isFiring)
        {
            return;
        }

        if (_buffer.Count < _minSamplesForLearning)
        {
            if (_bulletCount == _minSamplesForLearning - 1)
            {
                Debug.WriteLine($"[REALTIME DEBUG] Waiting for min samples: {_buffer.Count}/{_minSamplesForLearning}");
            }
            return;
        }

        // === PATTERN LOCK CHECK ===
        if (_bulletCount >= _patternLockThreshold && !_isPatternLocked)
        {
            LockPattern();
            Debug.WriteLine($"[REALTIME DEBUG] ✅ PATTERN LOCKED at bullet {_bulletCount}");
        }

        // === COMPENSATION CALCULATION ===
        double targetCompY, targetCompX;

        if (_isPatternLocked)
        {
            targetCompY = CalculatePredictiveCompensation(isVertical: true);
            targetCompX = CalculatePredictiveCompensation(isVertical: false);

            if (_bulletCount % 10 == 0)
            {
                Debug.WriteLine($"[REALTIME DEBUG] PREDICTIVE mode: base={_learnedBaseCompY:F3}");
            }
        }
        else
        {
            targetCompY = CalculateReactiveCompensation(isVertical: true);
            targetCompX = CalculateReactiveCompensation(isVertical: false);

            if (_bulletCount % 5 == 0)
            {
                Debug.WriteLine($"[REALTIME DEBUG] REACTIVE mode: targetCompY={targetCompY:F3}");
            }
        }

        // === ADAPTIVE SMOOTHING ===
        double alpha = AlphaFromTauMs(15.0, dt);
        _currentCompensationY = Lerp(_currentCompensationY, targetCompY, alpha);
        _currentCompensationX = Lerp(_currentCompensationX, targetCompX, alpha);

        // === APPLY COMPENSATION ===
        double finalCompY = _currentCompensationY * _adaptiveStrength;
        double finalCompX = _currentCompensationX * _adaptiveStrength;

        if (_bulletCount % 5 == 0)
        {
            Debug.WriteLine($"[REALTIME DEBUG] === APPLYING COMPENSATION ===");
            Debug.WriteLine($"  currentCompY: {_currentCompensationY:F3}");
            Debug.WriteLine($"  adaptiveStrength: {_adaptiveStrength:F2}");
            Debug.WriteLine($"  finalCompY: {finalCompY:F3}");
            Debug.WriteLine($"  output.RightY BEFORE: {output.RightY:F3}");
        }

        double newRightY = Math.Clamp(output.RightY + finalCompY, -1.0, 1.0);
        double newRightX = Math.Clamp(output.RightX + finalCompX, -1.0, 1.0);

        output = output with
        {
            RightY = newRightY,
            RightX = newRightX
        };

        if (_bulletCount % 5 == 0)
        {
            Debug.WriteLine($"  output.RightY AFTER: {output.RightY:F3}");
            Debug.WriteLine($"=======================================");
        }
    }

    /// <summary>
    /// ✅ FIX 3: REACTIVE MODE con segno corretto
    /// </summary>
    private double CalculateReactiveCompensation(bool isVertical)
    {
        var samples = _buffer.GetLast(50);
        if (samples.Length == 0) return 0.0;

        var firingSamples = samples.Where(s => s.IsFiring).ToArray();
        if (firingSamples.Length < 5) return 0.0;

        // Calcola velocità media MENTRE sparavi
        double avgVelocity = isVertical
            ? firingSamples.Average(s => s.VelocityY)
            : firingSamples.Average(s => s.VelocityX);

        // Trend
        double trend = 0.0;
        if (firingSamples.Length >= 10)
        {
            var firstHalf = firingSamples.Take(firingSamples.Length / 2);
            var secondHalf = firingSamples.Skip(firingSamples.Length / 2);

            double avgFirst = isVertical
                ? firstHalf.Average(s => s.VelocityY)
                : firstHalf.Average(s => s.VelocityX);

            double avgSecond = isVertical
                ? secondHalf.Average(s => s.VelocityY)
                : secondHalf.Average(s => s.VelocityX);

            trend = (avgSecond - avgFirst) * 0.5;
        }

        // ✅ Compensazione = OPPOSTO della velocità
        // Se rinculo tira SU (velocityY positivo) → compensazione NEGATIVA (tira giù)
        double compensation = -(avgVelocity + trend) * 0.1;

        // Fire rate adaptation
        if (_averageFireIntervalMs > 0 && _averageFireIntervalMs < 200)
        {
            double fireRateFactor = 100.0 / _averageFireIntervalMs;
            compensation *= Math.Clamp(fireRateFactor, 0.5, 1.5);
        }

        return compensation;
    }

    private double CalculatePredictiveCompensation(bool isVertical)
    {
        double baseComp = isVertical ? _learnedBaseCompY : _learnedBaseCompX;

        var recentSamples = _buffer.GetLast(20);
        if (recentSamples.Length >= 5)
        {
            var firingSamples = recentSamples.Where(s => s.IsFiring).ToArray();
            if (firingSamples.Length >= 3)
            {
                double recentAvg = isVertical
                    ? firingSamples.Average(s => s.VelocityY)
                    : firingSamples.Average(s => s.VelocityX);

                double adjustment = -recentAvg * 0.05;
                baseComp = Lerp(baseComp, baseComp + adjustment, _learningRate);
            }
        }

        return baseComp;
    }

    private void LockPattern()
    {
        var allSamples = _buffer.GetSnapshot();
        var firingSamples = allSamples.Where(s => s.IsFiring).ToArray();

        if (firingSamples.Length < 20)
            return;

        // ✅ Compensazione = OPPOSTO velocità media
        _learnedBaseCompY = -firingSamples.Average(s => s.VelocityY) * 0.1;
        _learnedBaseCompX = -firingSamples.Average(s => s.VelocityX) * 0.1;

        _detectedPattern = ClassifyPattern(firingSamples);

        _isPatternLocked = true;

        Debug.WriteLine($"[REALTIME RECOIL] Pattern locked: {_detectedPattern}, CompY={_learnedBaseCompY:F3}, CompX={_learnedBaseCompX:F3}");
    }

    private PatternType ClassifyPattern(RecoilSample[] samples)
    {
        if (samples.Length < 10)
            return PatternType.Unknown;

        double avgVelY = samples.Average(s => s.VelocityY);
        double variance = samples.Average(s => Math.Pow(s.VelocityY - avgVelY, 2));
        double stdDev = Math.Sqrt(variance);

        var firstHalf = samples.Take(samples.Length / 2).Average(s => s.VelocityY);
        var secondHalf = samples.Skip(samples.Length / 2).Average(s => s.VelocityY);
        double trend = Math.Abs(secondHalf - firstHalf);

        if (stdDev < 0.5 && trend < 0.3)
            return PatternType.Linear;

        if (trend > 1.0)
            return PatternType.Exponential;

        if (stdDev > 2.0)
            return PatternType.Random;

        return PatternType.Stepped;
    }

    private static double Lerp(double a, double b, double t) =>
        a + (b - a) * Math.Clamp(t, 0.0, 1.0);

    private static double AlphaFromTauMs(double tauMs, double dtSec)
    {
        double tau = tauMs / 1000.0;
        return (tau <= 1e-6) ? 1.0 : 1.0 - Math.Exp(-dtSec / tau);
    }
}