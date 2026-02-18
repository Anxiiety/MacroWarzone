using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone;


    public sealed class EwmaFilter
    {
        private double _y;
        private bool _has;
        private readonly double _alpha;

        public EwmaFilter(double cutoffHz, double sampleRateHz)
        {
            cutoffHz = Math.Max(0.001, cutoffHz);
            sampleRateHz = Math.Max(1.0, sampleRateHz);

            _alpha = 1.0 - Math.Exp(-2.0 * Math.PI * cutoffHz / sampleRateHz);
            _alpha = Math.Clamp(_alpha, 0.0, 1.0);
        }

        public double Step(double x)
        {
            if (!_has) { _y = x; _has = true; return x; }
            _y = _alpha * x + (1.0 - _alpha) * _y;
            return _y;
        }

        public void Reset() { _has = false; _y = 0; }
    }

