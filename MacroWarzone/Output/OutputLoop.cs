using MacroWarzone;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MacroWarzone.Macros;

/// <summary>
/// OutputLoop: thread realtime a 200Hz senza weapon switching.
/// </summary>
public sealed class OutputLoop
{
    private readonly ConfigRoot _cfg;
    private readonly GameProfile _profile;
    private readonly RawInputState _raw;
    private readonly ViGEmOutput _out;
    private readonly StickProcessor _hipLeft, _hipRight, _adsLeft, _adsRight;
    private readonly MacroConfiguration _draftConfig;

    private List<IMacroRule> _macros;
    private volatile bool _running = true;

    public OutputLoop(
        ConfigRoot cfg,
        GameProfile profile,
        RawInputState raw,
        ViGEmOutput output,
        StickProcessor hipLeft, StickProcessor hipRight,
        StickProcessor adsLeft, StickProcessor adsRight,
        List<IMacroRule> macros,
        MacroConfiguration macroConfiguration)
    {
        _cfg = cfg;
        _profile = profile;
        _raw = raw;
        _out = output;
        _hipLeft = hipLeft;
        _hipRight = hipRight;
        _adsLeft = adsLeft;
        _adsRight = adsRight;
        _macros = macros;
        _draftConfig = macroConfiguration;
    }

    public void Stop() => _running = false;

    /// <summary>
    /// Ricarica macro in runtime (hot-reload).
    /// Thread-safe: atomic swap.
    /// </summary>
    public void ReloadMacros(List<IMacroRule> newMacros)
    {
        _macros = newMacros;
    }

    public void Run()
    {
        long tickTicks = (long)(_cfg.TickMs * (Stopwatch.Frequency / 1000.0));
        long nextTick = Stopwatch.GetTimestamp() + tickTicks;

        var spin = new SpinWait();
        long prevTs = 0;

        while (_running)
        {
            long now = Stopwatch.GetTimestamp();
            long delta = nextTick - now;

            // ===== TIMING PRECISION =====
            if (delta > 0)
            {
                long oneMs = Stopwatch.Frequency / 1000;
                if (delta > 3 * oneMs)
                    Thread.Sleep(1);
                else
                    spin.SpinOnce();
                continue;
            }

            nextTick += tickTicks;

            // Lag recovery (se troppo indietro, salta tick)
            long maxLag = 5 * tickTicks;
            long after = Stopwatch.GetTimestamp();
            if (after - nextTick > maxLag)
                nextTick = after + tickTicks;

            // ===== 1. SNAPSHOT INPUT =====
            var s = _raw.GetSnapshot();

            long ts = Stopwatch.GetTimestamp();
            long dt = (prevTs == 0) ? 0 : (ts - prevTs);
            prevTs = ts;

            // ===== 2. TRIGGER NOISE + ADS =====
            byte l2 = ActivationModel.ApplyTriggerNoise(s.L2, _profile.Activation);
            byte r2 = ActivationModel.ApplyTriggerNoise(s.R2, _profile.Activation);

            bool isAds = ActivationModel.IsAds(r2, _profile.Activation);

            var set = isAds ? _profile.Ads : _profile.Hip;
            var leftProc = isAds ? _adsLeft : _hipLeft;
            var rightProc = isAds ? _adsRight : _hipRight;

            // ===== 3. NORMALIZE =====
            double lx = AxisMath.NormalizeAxis(s.Lx);
            double ly = -AxisMath.NormalizeAxis(s.Ly) * (set.Left.InvertY ? -1 : 1);

            double rx = AxisMath.NormalizeAxis(s.Rx);
            double ry = -AxisMath.NormalizeAxis(s.Ry) * (set.Right.InvertY ? -1 : 1);

            // ===== 4. PROCESS (deadzone/expo/smoothing) =====
            (lx, ly) = leftProc.Process(lx, ly);
            (rx, ry) = rightProc.Process(rx, ry);

            // ===== 5. INTENT GATE =====
            if (!ActivationModel.HasIntent(rx, ry, _profile.Activation))
            {
                // Opzionale: blocca drift
            }

            // ===== 6. BUILD OUTPUT STATE =====
            var o = new OutputState(
                LeftX: lx, LeftY: ly,
                RightX: rx, RightY: ry,
                L2: l2, R2: r2,
                L1: s.L1, R1: s.R1,
                Triangle: s.Triangle, Square: s.Square, Cross: s.Cross, Circle: s.Circle,
                DUp: s.DUp, DDown: s.DDown, DLeft: s.DLeft, DRight: s.DRight,
                Options: s.Options, Share: s.Share,
                L3: s.L3, R3: s.R3,
                TouchClick: s.TouchClick
            );

            // ===== 7. APPLY MACROS =====
            foreach (var rule in _macros)
                rule.Apply(in s, ref o);

            // ===== 8. SEND TO VIGEM =====
            _out.Send(in o);
        }
    }
}