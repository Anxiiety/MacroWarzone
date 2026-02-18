using System;
using System.Diagnostics;

namespace MacroWarzone.Macros
{
    /// <summary>
    /// Rapid Fire: Quando tieni premuto R1 (o R2), simula click ripetuti ad alta velocità.
    /// 
    /// Logica:
    /// - Trigger premuto → genera pulse ON/OFF a frequenza fissa
    /// - Utile per armi semi-automatiche (pistole, DMR, ecc.)
    /// - Frequenza tipica: 10-20 Hz (100-50ms tra click)
    /// 
    /// Design:
    /// - Input: R1 o R2 tenuto premuto
    /// - Output: R1 pulse (true/false alternato)
    /// </summary>
    public sealed class RapidFireRule : IMacroRule
    {
        private readonly Func<RawInputState.Snapshot, bool> _activationCondition;
        private readonly int _fireRateHz;  // Frequenza click al secondo (es: 15 Hz = 15 click/sec)

        // Stato interno
        private long _lastToggleTicks;
        private bool _currentState;  // true = R1 premuto, false = R1 rilasciato

        /// <summary>
        /// Costruttore Rapid Fire
        /// </summary>
        /// <param name="activationCondition">Condizione per attivare (es: R1 tenuto)</param>
        /// <param name="fireRateHz">Frequenza click al secondo (default 15 Hz = 66ms tra click)</param>
        public RapidFireRule(
            Func<RawInputState.Snapshot, bool> activationCondition,
            int fireRateHz = 15)
        {
            _activationCondition = activationCondition;
            _fireRateHz = Math.Clamp(fireRateHz, 5, 30);  // Min 5 Hz, max 30 Hz
        }

        public void Apply(in RawInputState.Snapshot input, ref OutputState output)
        {
            bool shouldFire = _activationCondition(input);

            if (!shouldFire)
            {
                // Trigger non premuto → reset stato
                _currentState = false;
                _lastToggleTicks = 0;
                return;
            }

            // Calcola intervallo tra toggle (in ticks)
            long now = Stopwatch.GetTimestamp();
            long toggleIntervalTicks = Stopwatch.Frequency / _fireRateHz;

            // Prima attivazione
            if (_lastToggleTicks == 0)
            {
                _lastToggleTicks = now;
                _currentState = true;  // Inizia con R1 premuto
            }

            // Verifica se è ora di fare toggle
            if (now - _lastToggleTicks >= toggleIntervalTicks)
            {
                _currentState = !_currentState;  // Alterna ON/OFF
                _lastToggleTicks = now;
            }

            // Applica pulse a R1
            output = output with { R1 = _currentState };
        }
    }
}