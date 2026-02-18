using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone;

   
    public static class ActivationModel
    {
        public static bool IsAds(byte r2, ActivationConfig a)
            => r2 >= a.AdsWhenR2Above;

        public static bool HasIntent(double x, double y, ActivationConfig a)
            => Math.Sqrt(x * x + y * y) >= a.MinIntentMagnitude;

        public static byte ApplyTriggerNoise(byte v, ActivationConfig a)
            => v <= a.TriggerNoiseThreshold ? (byte)0 : v;
    }

