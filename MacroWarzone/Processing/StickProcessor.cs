using System;

namespace MacroWarzone;

public sealed class StickProcessor
{
    private readonly AxisConfig _cfg;
    private readonly EwmaFilter? _fx;
    private readonly EwmaFilter? _fy;

    public StickProcessor(AxisConfig cfg, double sampleRateHz)
    {
        _cfg = cfg;

        if (string.Equals(cfg.Smoothing.Type, "ewma", StringComparison.OrdinalIgnoreCase))
        {
            _fx = new EwmaFilter(cfg.Smoothing.CutoffHz, sampleRateHz);
            _fy = new EwmaFilter(cfg.Smoothing.CutoffHz, sampleRateHz);
        }
    }

    public (double x, double y) Process(double x, double y)
    {
        (x, y) = AxisMath.ApplyRadialDeadzone(x, y, _cfg.Deadzone);
        x = AxisMath.Expo(x, _cfg.Expo);
        y = AxisMath.Expo(y, _cfg.Expo);

        if (_fx != null && _fy != null)
        {
            x = _fx.Step(x);
            y = _fy.Step(y);
        }

        return (x, y);
    }
}