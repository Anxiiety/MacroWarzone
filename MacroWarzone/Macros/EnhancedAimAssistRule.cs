using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MacroWarzone;
using System.Linq;




namespace MacroWarzone.Macros
{
    /// <summary>
    /// Aim Assist Potenziato: simula il comportamento moderno dell'aim assist
    /// con rotazione assistita, slowdown e drag, attivabile con L1+R1.
    /// 
    /// Logica chiave:
    /// - Rotazione assistita: richiede movimento laterale (left stick) + movimento camera (right stick) + target in movimento
    /// - Slowdown: riduce sensibilità quando vicino al target
    /// - Drag: segue il target quando già puntato
    /// - Sistema molto selettivo: input deboli = nessuna assistenza
    /// </summary>
    public sealed class EnhancedAimAssistRule : IMacroRule
    {
        private readonly Func<RawInputState.Snapshot, bool> _activationCondition;

        // === PARAMETRI ROTAZIONE ASSISTITA ===
        private readonly double _rotationStrength;           // Forza correzione angolare (0.0-1.0)
        private readonly double _rotationConeAngle;          // Angolo max del cono di assistenza (gradi)
        private readonly double _strafeThreshold;            // Soglia minima movimento laterale
        private readonly double _cameraThreshold;            // Soglia minima movimento camera
        private readonly double _targetMotionThreshold;      // Soglia minima movimento target simulato

        // === PARAMETRI SLOWDOWN ===
        private readonly double _slowdownRadius;             // Raggio zona slowdown (normalizzato)
        private readonly double _slowdownStrength;           // Quanto ridurre sensibilità (0.0-1.0)
        private readonly double _slowdownSmoothMs;           // Smoothing del slowdown

        // === PARAMETRI DRAG ===
        private readonly double _dragStrength;               // Forza tracking del target
        private readonly double _dragRadius;                 // Raggio zona drag
        private readonly double _dragSmoothMs;               // Smoothing del drag

        // === PARAMETRI ADS vs HIP-FIRE ===
        private readonly Func<RawInputState.Snapshot, bool> _isADS;
        private readonly double _adsRotationMult;            // Moltiplicatore rotazione in ADS
        private readonly double _adsDragMult;                // Moltiplicatore drag in ADS

        // === STATO INTERNO ===
        private long _lastTicks;
        private double _prevLeftX, _prevLeftY;
        private double _prevRightX, _prevRightY;

        // Simulazione target (in assenza di dati reali, usiamo pattern sintetico)
        private double _targetX, _targetY;                   // Posizione target simulata
        private double _targetVelX, _targetVelY;             // Velocità target simulata
        private long _targetUpdateTicks;

        // Stato slowdown/drag
        private double _currentSlowdown;
        private double _currentDragX, _currentDragY;

        // Metriche attivazione
        private bool _rotationActive;
private bool _rotationEnabled;
        public EnhancedAimAssistRule(
            Func<RawInputState.Snapshot, bool> activationCondition,
            Func<RawInputState.Snapshot, bool> isADS = null,
            // Rotazione
            double rotationStrength = 0.12,
            double rotationConeAngle = 25.0,
            double strafeThreshold = 0.15,
            double cameraThreshold = 0.08,
            double targetMotionThreshold = 0.05,
            // Slowdown
            double slowdownRadius = 0.25,
            double slowdownStrength = 0.65,
            double slowdownSmoothMs = 40,
            // Drag
            double dragStrength = 0.18,
            double dragRadius = 0.20,
            double dragSmoothMs = 35,
            // ADS multipliers
            double adsRotationMult = 0.5,
            double adsDragMult = 1.3)
        {
            _activationCondition = activationCondition;
            _isADS = isADS ?? (_ => false);

            _rotationStrength = Math.Clamp(rotationStrength, 0.0, 1.0);
            _rotationConeAngle = Math.Clamp(rotationConeAngle, 5.0, 90.0);
            _strafeThreshold = Math.Max(0.0, strafeThreshold);
            _cameraThreshold = Math.Max(0.0, cameraThreshold);
            _targetMotionThreshold = Math.Max(0.0, targetMotionThreshold);

            _slowdownRadius = Math.Clamp(slowdownRadius, 0.05, 1.0);
            _slowdownStrength = Math.Clamp(slowdownStrength, 0.0, 1.0);
            _slowdownSmoothMs = Math.Max(5, slowdownSmoothMs);

            _dragStrength = Math.Clamp(dragStrength, 0.0, 1.0);
            _dragRadius = Math.Clamp(dragRadius, 0.05, 1.0);
            _dragSmoothMs = Math.Max(5, dragSmoothMs);

            _adsRotationMult = Math.Clamp(adsRotationMult, 0.1, 2.0);
            _adsDragMult = Math.Clamp(adsDragMult, 0.1, 2.0);
        }

        public void Apply(in RawInputState.Snapshot input, ref OutputState output)
        {
            if (!_activationCondition(input))
            {
                ResetState();
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (_lastTicks == 0)
            {
                _lastTicks = now;
                InitializeState(output);
                return;
            }

            double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
            _lastTicks = now;

            bool ads = _isADS(input);

            // === 1. AGGIORNA SIMULAZIONE TARGET ===
            UpdateTargetSimulation(dt, output);

            // === 2. CALCOLA METRICHE INPUT ===
            double leftX = output.LeftX;
            double leftY = output.LeftY;
            double rightX = output.RightX;
            double rightY = output.RightY;

            // Movimento laterale (strafe)
            double strafeMag = Math.Abs(leftX);
            double strafeVelX = (leftX - _prevLeftX) / Math.Max(dt, 0.001);

            // Movimento camera
            double cameraMag = Math.Sqrt(rightX * rightX + rightY * rightY);

            // Distanza dal target simulato (centro schermo = 0,0)
            double distToTarget = Math.Sqrt(_targetX * _targetX + _targetY * _targetY);

            // Velocità target
            double targetMotion = Math.Sqrt(_targetVelX * _targetVelX + _targetVelY * _targetVelY);

            // === 3. ROTAZIONE ASSISTITA ===
            double rotationX = 0.0, rotationY = 0.0;
            _rotationActive = false;

            // Condizioni necessarie (tutte devono essere vere)
            bool strafeOk = strafeMag >= _strafeThreshold;
            bool cameraOk = cameraMag >= _cameraThreshold;
            bool targetOk = targetMotion >= _targetMotionThreshold;
            bool inCone = IsInAssistCone(rightX, rightY, _targetX, _targetY);

            if (strafeOk && cameraOk && targetOk && inCone)
            {
                _rotationActive = true;

                // Calcola correzione angolare
                double errorX = _targetX - rightX * 0.5; // Right stick già punta parzialmente
                double errorY = _targetY - rightY * 0.5;

                // Forza dipende da: strafe + movimento target
                double rotationFactor = _rotationStrength;
                rotationFactor *= Math.Clamp(strafeMag / 0.5, 0.3, 1.0);
                rotationFactor *= Math.Clamp(targetMotion / 0.2, 0.2, 1.0);

                // In ADS ridotto
                if (ads) rotationFactor *= _adsRotationMult;

                // Decadimento con distanza (più lontano = meno assistenza)
                double distanceFalloff = 1.0 / (1.0 + distToTarget * 2.0);
                rotationFactor *= distanceFalloff;

                rotationX = errorX * rotationFactor;
                rotationY = errorY * rotationFactor;
            }

            // === 4. SLOWDOWN ===
            double targetSlowdown = 1.0;
            if (distToTarget < _slowdownRadius)
            {
                // Più vicino al target = più slowdown
                double slowdownFactor = 1.0 - (distToTarget / _slowdownRadius);
                targetSlowdown = 1.0 - (_slowdownStrength * slowdownFactor);
            }

            double alphaSlowdown = AlphaFromTauMs(_slowdownSmoothMs, dt);
            _currentSlowdown = Lerp(_currentSlowdown, targetSlowdown, alphaSlowdown);

            // === 5. DRAG ===
            double targetDragX = 0.0, targetDragY = 0.0;
            if (distToTarget < _dragRadius && targetMotion > 0.01)
            {
                // Segue il movimento del target
                double dragFactor = _dragStrength;

                // Più vicino al centro = più drag
                double dragFalloff = 1.0 - (distToTarget / _dragRadius);
                dragFactor *= dragFalloff;

                // In ADS più forte
                if (ads) dragFactor *= _adsDragMult;

                targetDragX = _targetVelX * dragFactor;
                targetDragY = _targetVelY * dragFactor;
            }

            double alphaDrag = AlphaFromTauMs(_dragSmoothMs, dt);
            _currentDragX = Lerp(_currentDragX, targetDragX, alphaDrag);
            _currentDragY = Lerp(_currentDragY, targetDragY, alphaDrag);

            // === 6. APPLICA TUTTO ===
            // Applica rotazione assistita
            double newRightX = rightX + rotationX;
            double newRightY = rightY + rotationY;

            // Applica drag
            newRightX += _currentDragX;
            newRightY += _currentDragY;

            // Applica slowdown (riduce la sensibilità)
            newRightX *= _currentSlowdown;
            newRightY *= _currentSlowdown;

            // Clamp finale
            newRightX = Math.Clamp(newRightX, -1.0, 1.0);
            newRightY = Math.Clamp(newRightY, -1.0, 1.0);

            output = output with { RightX = newRightX, RightY = newRightY };

            // Salva stato
            _prevLeftX = leftX;
            _prevLeftY = leftY;
            _prevRightX = rightX;
            _prevRightY = rightY;
        }

        private void UpdateTargetSimulation(double dt, OutputState output)
        {
            // Simula un target che si muove in modo pseudo-casuale
            // In un sistema reale, questa info verrebbe dal gioco

            long now = Stopwatch.GetTimestamp();
            if (_targetUpdateTicks == 0)
            {
                _targetUpdateTicks = now;
                // Inizializza target leggermente fuori centro
                _targetX = 0.15;
                _targetY = 0.10;
            }

            double timeSinceUpdate = (now - _targetUpdateTicks) / (double)Stopwatch.Frequency;

            // Aggiorna velocità target ogni 0.3-0.8 secondi (simula movimento nemico)
            if (timeSinceUpdate > 0.5)
            {
                _targetUpdateTicks = now;

                // Pattern: movimento laterale con variazione verticale
                Random rnd = new Random((int)(now & 0xFFFFFF));
                _targetVelX = (rnd.NextDouble() - 0.5) * 0.4;
                _targetVelY = (rnd.NextDouble() - 0.5) * 0.2;
            }

            // Aggiorna posizione target
            _targetX += _targetVelX * dt;
            _targetY += _targetVelY * dt;

            // Mantieni target entro limiti schermo
            if (Math.Abs(_targetX) > 0.8) _targetVelX *= -1;
            if (Math.Abs(_targetY) > 0.6) _targetVelY *= -1;

            _targetX = Math.Clamp(_targetX, -1.0, 1.0);
            _targetY = Math.Clamp(_targetY, -1.0, 1.0);
        }

        private bool IsInAssistCone(double rightX, double rightY, double targetX, double targetY)
        {
            // Verifica se il movimento della camera è entro il cono di assistenza
            double rightMag = Math.Sqrt(rightX * rightX + rightY * rightY);
            if (rightMag < 0.001) return true; // Se non ti muovi, considera sempre valido

            double toTargetX = targetX - rightX * 0.5;
            double toTargetY = targetY - rightY * 0.5;
            double toTargetMag = Math.Sqrt(toTargetX * toTargetX + toTargetY * toTargetY);

            if (toTargetMag < 0.001) return true; // Già sul target

            // Dot product per angolo
            double dot = (rightX * toTargetX + rightY * toTargetY) / (rightMag * toTargetMag);
            double angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);

            return angle <= _rotationConeAngle;
        }

        private void InitializeState(OutputState output)
        {
            _prevLeftX = output.LeftX;
            _prevLeftY = output.LeftY;
            _prevRightX = output.RightX;
            _prevRightY = output.RightY;
            _currentSlowdown = 1.0;
        }

        private void ResetState()
        {
            _lastTicks = 0;
            _targetUpdateTicks = 0;
            _currentSlowdown = 1.0;
            _currentDragX = 0.0;
            _currentDragY = 0.0;
            _rotationActive = false;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Clamp(t, 0.0, 1.0);
        }

        private static double AlphaFromTauMs(double tauMs, double dtSec)
        {
            double tau = tauMs / 1000.0;
            if (tau <= 1e-6) return 1.0;
            return 1.0 - Math.Exp(-dtSec / tau);
        }
    }
}