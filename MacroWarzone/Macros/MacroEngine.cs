using MacroWarzone.Macros;
using System;
using System.Collections.Generic;

namespace MacroWarzone;

public static class MacroEngine
{
    public static List<IMacroRule> BuildRulesFromConfig(MacroConfiguration config)
    {
        var rules = new List<IMacroRule>();

        if (config.RealtimeAntiRecoil?.Enabled == true)
        {
            var cfg = config.RealtimeAntiRecoil;
            rules.Add(new RealtimeAntiRecoilRule(
                fireCondition: MacroTriggerParser.ParseTrigger(cfg.Trigger),
                adaptiveStrength: cfg.AdaptiveStrength,
                learningRate: cfg.LearningRate,
                bufferSize: cfg.BufferSize,
                minSamplesForLearning: cfg.MinSamplesForLearning,
                patternLockThreshold: cfg.PatternLockThreshold
            ));
        }
        else if (config.AntiRecoil?.Enabled == true)
        {
            var cfg = config.AntiRecoil;
            rules.Add(new AntiRecoilRule(
                fireCondition: MacroTriggerParser.ParseTrigger(cfg.Trigger),
                recoilStrength: cfg.RecoilStrength,
                verticalBias: cfg.VerticalBias,
                horizontalBias: cfg.HorizontalBias,
                smoothingTauMs: cfg.SmoothingTauMs,
                rampUpMs: cfg.RampUpMs,
                rampDownMs: cfg.RampDownMs
            ));
        }

        if (config.AimAssist?.Enabled == true)
        {
            var cfg = config.AimAssist;
            rules.Add(new EnhancedAimAssistRule(
                activationCondition: MacroTriggerParser.ParseTrigger(cfg.ActivationTrigger),
                isADS: MacroTriggerParser.ParseTrigger(cfg.ADSTrigger),
                rotationStrength: cfg.RotationStrength,
                rotationConeAngle: cfg.RotationConeAngle,
                strafeThreshold: cfg.StrafeThreshold,
                cameraThreshold: cfg.CameraThreshold,
                targetMotionThreshold: cfg.TargetMotionThreshold,
                slowdownRadius: cfg.SlowdownRadius,
                slowdownStrength: cfg.SlowdownStrength,
                slowdownSmoothMs: cfg.SlowdownSmoothMs,
                dragStrength: cfg.DragStrength,
                dragRadius: cfg.DragRadius,
                dragSmoothMs: cfg.DragSmoothMs,
                adsRotationMult: cfg.AdsRotationMult,
                adsDragMult: cfg.AdsDragMult
            ));
        }
        if (config.ZenCronusAimAssist?.Enabled == true)
        {
            var cfg = config.ZenCronusAimAssist;
            rules.Add(new ZenCronusAimAssistRule(
                enabled: MacroTriggerParser.ParseTrigger(cfg.ActivationTrigger),
                isADS: MacroTriggerParser.ParseTrigger(cfg.ADSTrigger),

                bubbleRadius: cfg.BubbleRadius,
                bubbleStrength: cfg.BubbleStrength,
                bubbleSmoothMs: cfg.BubbleSmoothMs,

                microCorrectionRadius: cfg.MicroCorrectionRadius,
                microCorrectionStrength: cfg.MicroCorrectionStrength,
                microCorrectionSmoothMs: cfg.MicroCorrectionSmoothMs,

                shakeThreshold: cfg.ShakeThreshold,
                shakeDampening: cfg.ShakeDampening,

                adsMultiplier: cfg.AdsMultiplier,

                useResponseOverride: cfg.UseResponseOverride,
                responseCenterBoost: cfg.ResponseCenterBoost
            ));
        }

        if (config.AutoPing?.Enabled == true)
        {
            var cfg = config.AutoPing;
            rules.Add(new AutoPingRule(
                activationCondition: MacroTriggerParser.ParseTrigger(cfg.Trigger),
                pingDurationMs: cfg.PingDurationMs
            ));
        }

        if (config.IsUsingSniper?.Enabled == true)
        {
            var cfg = config.IsUsingSniper;
            rules.Add(new IsUsingSniperRule(
                activationCondition: MacroTriggerParser.ParseTrigger(cfg.Trigger)
            ));
        }

        if (config.RapidFire?.Enabled == true)
        {
            var cfg = config.RapidFire;
            rules.Add(new RapidFireRule(
                activationCondition: MacroTriggerParser.ParseTrigger(cfg.Trigger),
                fireRateHz: cfg.FireRateHz
            ));
        }

        return rules;
    }
}
