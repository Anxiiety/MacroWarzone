using System;
using System.Diagnostics;

namespace MacroWarzone.Macros
{
    /// <summary>
    /// AntiRecoil: Compensa automaticamente il rinculo verticale e orizzontale
    /// quando spari (tipicamente R1 premuto). 
    /// Design:
    /// - Input: Condizione di sparo (es: R1 premuto)
    /// - Output: Modifica RightY (verticale) e RightX (orizzontale) del right stick
    /// </summary>
    public sealed class AntiRecoilRule : IMacroRule
    {
        private readonly Func<RawInputState.Snapshot, bool> _fireCondition;

        // Parametri compensazione
        private readonly double _recoilStrength;      // Forza compensazione (0.0-1.0)
        private readonly double _verticalBias;        // Direzione verticale (-1.0 = giù, +1.0 = su)
        private readonly double _horizontalBias;      // Direzione orizzontale (-1.0 = sinistra, +1.0 = destra)

        // Parametri temporali
        private readonly double _smoothingTauMs;      // Smoothing della compensazione (ms)
        private readonly int _rampUpMs;               // Tempo per raggiungere forza massima (ms)
        private readonly int _rampDownMs;             // Tempo per tornare a zero (ms)

        // Stato interno
        private long _lastTicks;
        private long _fireStartTicks;                 // Quando hai iniziato a sparare
        private double _currentCompensationY;         // Compensazione Y corrente (smoothed)
        private double _currentCompensationX;         // Compensazione X corrente (smoothed)
        private double _rampMultiplier;               // Moltiplicatore ramp (0.0-1.0)
        private bool _wasFiring;

        public AntiRecoilRule(
            Func<RawInputState.Snapshot, bool> fireCondition,
            double recoilStrength = 0.24,
            double verticalBias = -1.0,               // Default: compensa verso il basso
            double horizontalBias = 0.0,              // Default: nessuna deriva orizzontale
            double smoothingTauMs = 25,
            int rampUpMs = 120,
            int rampDownMs = 80)
        {
            _fireCondition = fireCondition;

            _recoilStrength = Math.Clamp(recoilStrength, 0.0, 1.5);
            _verticalBias = Math.Clamp(verticalBias, -1.0, 1.0);
            _horizontalBias = Math.Clamp(horizontalBias, -1.0, 1.0);

            _smoothingTauMs = Math.Max(5, smoothingTauMs);
            _rampUpMs = Math.Max(10, rampUpMs);
            _rampDownMs = Math.Max(10, rampDownMs);
        }

        public void Apply(in RawInputState.Snapshot input, ref OutputState output)
        {
            long now = Stopwatch.GetTimestamp();

            // Prima esecuzione: inizializza
            if (_lastTicks == 0)
            {
                _lastTicks = now;
                return;
            }

            double dt = (now - _lastTicks) / (double)Stopwatch.Frequency;
            _lastTicks = now;

            bool isFiring = _fireCondition(input);

            // === RAMP-UP / RAMP-DOWN ===
            if (isFiring)
            {
                // Appena iniziato a sparare
                if (!_wasFiring)
                {
                    _fireStartTicks = now;
                }

                // Calcola quanto tempo stai sparando (in secondi)
                double fireTime = (now - _fireStartTicks) / (double)Stopwatch.Frequency;
                double rampUpTime = _rampUpMs / 1000.0;

                // Ramp-up progressivo (0.0 → 1.0)
                _rampMultiplier = Math.Clamp(fireTime / rampUpTime, 0.0, 1.0);
            }
            else
            {
                // Non stai sparando: ramp-down
                double rampDownTime = _rampDownMs / 1000.0;
                double decayRate = dt / rampDownTime;

                _rampMultiplier = Math.Max(0.0, _rampMultiplier - decayRate);
            }

            // === CALCOLA COMPENSAZIONE TARGET ===
            // La compensazione è proporzionale a:
            // - recoilStrength (quanto forte)
            // - bias (direzione)
            // - rampMultiplier (crescita/decadimento graduale)

            double targetCompY = _recoilStrength * _verticalBias * _rampMultiplier;
            double targetCompX = _recoilStrength * _horizontalBias * _rampMultiplier;

            // === SMOOTHING (filtro esponenziale) ===
            // Evita scatti improvvisi, rende la compensazione fluida
            double alpha = AlphaFromTauMs(_smoothingTauMs, dt);

            _currentCompensationY = Lerp(_currentCompensationY, targetCompY, alpha);
            _currentCompensationX = Lerp(_currentCompensationX, targetCompX, alpha);

            // === APPLICA AL RIGHT STICK ===
            // Nota: in gioco, tirare giù il right stick = guardare in basso
            // Per compensare rinculo verso l'alto, devi tirare il bastone verso il basso
            // Quindi: verticalBias = -1.0 → compensazione verso il basso (contrasta rinculo up)

            double newRightY = output.RightY + _currentCompensationY;
            double newRightX = output.RightX + _currentCompensationX;

            // Clamp per sicurezza (range stick: -1.0 a +1.0)
            newRightY = Math.Clamp(newRightY, -1.0, 1.0);
            newRightX = Math.Clamp(newRightX, -1.0, 1.0);

            output = output with
            {
                RightY = newRightY,
                RightX = newRightX
            };

            _wasFiring = isFiring;
        }

        // === HELPER METHODS ===

        /// <summary>
        /// Interpolazione lineare con clamp
        /// </summary>
        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Clamp(t, 0.0, 1.0);
        }

        /// <summary>
        /// Calcola alpha per filtro esponenziale da time constant (tau)
        /// Formula: alpha = 1 - exp(-dt / tau)
        /// </summary>
        private static double AlphaFromTauMs(double tauMs, double dtSec)
        {
            double tau = tauMs / 1000.0;
            if (tau <= 1e-6) return 1.0;
            return 1.0 - Math.Exp(-dtSec / tau);
        }
    }
}