using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MacroWarzone.Macros
{
    /// <summary>
    /// AutoPing: Quando premi L1+R1 insieme, pinga il nemico UNA SOLA VOLTA
    /// premendo D-Pad Up automaticamente.
    /// 
    /// Logica:
    /// - Rileva quando passi da "non premuto" a "premuto" (edge detection)
    /// - Pinga SOLO al momento dell'attivazione
    /// - Se continui a tenere premuto, NON pinga di nuovo
    /// - Solo quando rilasci E ripremi, pinga di nuovo
    /// </summary>
    public sealed class AutoPingRule : IMacroRule
    {
        private readonly Func<RawInputState.Snapshot, bool> _activationCondition;
        private readonly int _pingDurationMs;

        // Stato interno
        private bool _wasPressed;           // Stato precedente del trigger
        private bool _pingActive;           // Se il ping è attualmente attivo
        private long _pingEndTicks;         // Quando finisce il ping

        public AutoPingRule(
            Func<RawInputState.Snapshot, bool> activationCondition,
            int pingDurationMs = 100)       // Durata pressione D-Pad Up (100ms default)
        {
            _activationCondition = activationCondition;
            _pingDurationMs = Math.Max(50, pingDurationMs);
        }

        public void Apply(in RawInputState.Snapshot input, ref OutputState output)
        {
            long now = Stopwatch.GetTimestamp();
            bool isPressed = _activationCondition(input);

            if (isPressed && !_wasPressed)
            {
                Console.WriteLine($"[AutoPingRule] TRIGGER ACTIVATED");
            }

            // === EDGE DETECTION: Rileva il momento esatto in cui premi ===
            bool justPressed = isPressed && !_wasPressed;

            if (justPressed)
            {
                // Appena premuto L1+R1: Attiva il ping
                _pingActive = true;
                long ticksPerMs = Stopwatch.Frequency / 1000;
                _pingEndTicks = now + (_pingDurationMs * ticksPerMs);
            }

            // === Mantieni D-Pad Up premuto per la durata ===
            if (_pingActive)
            {
                if (now < _pingEndTicks)
                {
                    // Ancora dentro la finestra del ping
                    output = output with { DUp = true };
                }
                else
                {
                    // Ping completato
                    _pingActive = false;
                }
            }

            // Salva stato per il prossimo frame
            _wasPressed = isPressed;
        }
    }

}
