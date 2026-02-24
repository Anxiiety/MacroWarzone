using MacroWarzone.Macros;
using MacroWarzone.Vision;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MacroWarzone;

public static class MacroEngine
{
    public static List<IMacroRule> BuildRulesFromConfig(MacroConfiguration config)
    {
        var rules = new List<IMacroRule>();

        if (config.RealtimeAntiRecoil?.Enabled == true)
        {

            Debug.WriteLine("[MACRO ENGINE] ✅ RealtimeAntiRecoil ENABLED, building rule...");
            
            Debug.WriteLine($"[MACRO ENGINE] RealtimeAntiRecoil rule added. Total rules: {rules.Count}");

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

        // === AI VISION AIM ASSIST ===
        if (config.AIVisionAimAssist?.Enabled == true)
        {
            var cfg = config.AIVisionAimAssist;

            Debug.WriteLine("[MACRO ENGINE] Initializing AI Vision components...");

            // Screen Capture
            var capture = new ScreenCaptureService();
            bool captureOk = capture.Initialize();
            Debug.WriteLine($"[MACRO ENGINE] Screen Capture: {(captureOk ? "✓ OK" : "❌ FAILED")}");

            // AI Vision Model
            string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolov8n_warzone.onnx");

            // ✅ FALLBACK: Cerca anche in root directory
            if (!File.Exists(modelPath))
            {
                modelPath = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName, "models", "yolov8n_warzone.onnx");
            }

            var aiVision = new AIVisionService();
            bool visionOk = aiVision.Initialize(modelPath, cfg.UseGPU);
            Debug.WriteLine($"[MACRO ENGINE] AI Vision: {(visionOk ? "✓ OK" : "❌ FAILED")}");

            // ✅ CREA OVERLAY COMUNQUE (per test visivo)
            OverlayRenderer? overlay = null;
            try
            {
                Debug.WriteLine("[MACRO ENGINE] Creating overlay...");
                overlay = new OverlayRenderer();
                overlay.Initialize();

                if (overlay.IsVisible)
                {
                    Debug.WriteLine("[MACRO ENGINE] ✓ Overlay visible");
                }
                else
                {
                    Debug.WriteLine("[MACRO ENGINE] ⚠ Overlay created but not visible");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MACRO ENGINE] ❌ Overlay failed: {ex.Message}");
            }

            // ✅ AGGIUNGI RULE ANCHE SE QUALCOSA FALLISCE (per debug)
            if (captureOk && visionOk)
            {
                rules.Add(new AIVisionAimAssistRule(
                    enabled: MacroTriggerParser.ParseTrigger(cfg.ActivationTrigger),
                    isADS: MacroTriggerParser.ParseTrigger(cfg.ADSTrigger),
                    capture: capture,
                    aiVision: aiVision,
                    overlay: overlay,
                    assistStrength: cfg.AssistStrength,
                    smoothingMs: cfg.SmoothingMs,
                    maxRotationSpeed: cfg.MaxRotationSpeed,
                    reactionDelayMs: cfg.ReactionDelayMs
                ));

                Debug.WriteLine("[MACRO ENGINE] ✓ AI Vision Aim Assist rule added");
            }
            else
            {
                Debug.WriteLine($"[MACRO ENGINE] ⚠ Skipping AI Vision rule (capture={captureOk}, vision={visionOk})");

                // ✅ MA LASCIA OVERLAY APERTO PER TEST
                if (overlay != null && overlay.IsVisible)
                {
                    Debug.WriteLine("[MACRO ENGINE] Overlay still active for testing");
                }
            }
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
