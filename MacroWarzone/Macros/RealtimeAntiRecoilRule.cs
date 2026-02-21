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
    private double _prevRightY;
    private double _prevRightX;
    private bool _wasFiring;
    private int _bulletCount;
    private long _lastShotTicks;

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

        _buffer = new CircularBuffer<RecoilSample>(_bufferSize);
        _detectedPattern = PatternType.Unknown;
    }

    public void Apply(in RawInputState.Snapshot input, ref OutputState output)
    {
        long now = Stopwatch.GetTimestamp();
        bool isFiring = _fireCondition(input);

        if (_lastTicks == 0)
        {
            _lastTicks = now;
            _prevRightY = output.RightY;
            _prevRightX = output.RightX;
            _wasFiring = isFiring;
            return;
        }

        double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        if (isFiring && !_wasFiring)
        {
            _bulletCount = 0;
            _shotCountForFireRate = 0;
            _firstShotTicks = now;
        }

        if (!isFiring && _wasFiring)
        {
            _bulletCount = 0;
            _currentCompensationY = 0;
            _currentCompensationX = 0;
        }

        if (isFiring)
        {
            long timeSinceLastShot = now - _lastShotTicks;
            double intervalMs = timeSinceLastShot / (double)(Stopwatch.Frequency / 1000);

            if (intervalMs > 40 || _bulletCount == 0)
            {
                _bulletCount++;
                _shotCountForFireRate++;
                _lastShotTicks = now;

                if (_shotCountForFireRate > 1)
                {
                    double totalTimeMs = (now - _firstShotTicks) / (double)(Stopwatch.Frequency / 1000);
                    _averageFireIntervalMs = totalTimeMs / (_shotCountForFireRate - 1);
                }
            }
        }

        double deltaY = output.RightY - _prevRightY;
        double deltaX = output.RightX - _prevRightX;

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

        _prevRightY = output.RightY;
        _prevRightX = output.RightX;
        _wasFiring = isFiring;

        if (!isFiring || _buffer.Count < _minSamplesForLearning)
            return;

        if (_bulletCount >= _patternLockThreshold && !_isPatternLocked)
            LockPattern();

        double targetCompY = _isPatternLocked
            ? CalculatePredictiveCompensation(isVertical: true)
            : CalculateReactiveCompensation(isVertical: true);
        double targetCompX = _isPatternLocked
            ? CalculatePredictiveCompensation(isVertical: false)
            : CalculateReactiveCompensation(isVertical: false);

        double alpha = AlphaFromTauMs(15.0, dt);
        _currentCompensationY = Lerp(_currentCompensationY, targetCompY, alpha);
        _currentCompensationX = Lerp(_currentCompensationX, targetCompX, alpha);

        double finalCompY = _currentCompensationY * _adaptiveStrength;
        double finalCompX = _currentCompensationX * _adaptiveStrength;

        output = output with
        {
            RightY = Math.Clamp(output.RightY + finalCompY, -1.0, 1.0),
            RightX = Math.Clamp(output.RightX + finalCompX, -1.0, 1.0)
        };
    }

    private double CalculateReactiveCompensation(bool isVertical)
    {
        var samples = _buffer.GetLast(50);
        if (samples.Length == 0)
            return 0.0;

        var firingSamples = samples.Where(s => s.IsFiring).ToArray();
        if (firingSamples.Length < 5)
            return 0.0;

        double avgVelocity = isVertical
            ? firingSamples.Average(s => s.VelocityY)
            : firingSamples.Average(s => s.VelocityX);

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

        double compensation = -(avgVelocity + trend) * 0.1;

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

        double firstHalf = samples.Take(samples.Length / 2).Average(s => s.VelocityY);
        double secondHalf = samples.Skip(samples.Length / 2).Average(s => s.VelocityY);
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
