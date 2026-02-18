using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone;


    public sealed class GameProfile
    {
        public ResponseSet Hip { get; set; } = new();
        public ResponseSet Ads { get; set; } = new();
        public ActivationConfig Activation { get; set; } = new();
    }

    public sealed class ResponseSet
    {
        public AxisConfig Left { get; set; } = new();
        public AxisConfig Right { get; set; } = new();
    }

    public sealed class AxisConfig
    {
        public double Deadzone { get; set; } = 0.05;
        public double Expo { get; set; } = 0.0;

        public bool InvertY { get; set; } = false;

        public SmoothingConfig Smoothing { get; set; } = new();
    }

    public sealed class SmoothingConfig
    {
        public string Type { get; set; } = "none"; // "none" | "ewma"
        public double CutoffHz { get; set; } = 30;
    }

    public sealed class ActivationConfig
    {
        public int AdsWhenR2Above { get; set; } = 20;
        public double MinIntentMagnitude { get; set; } = 0.02;
        public byte TriggerNoiseThreshold { get; set; } = 3;
    }


