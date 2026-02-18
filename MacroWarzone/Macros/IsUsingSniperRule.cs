using System;

namespace MacroWarzone.Macros
{
    /// <summary>
    /// IsUsingSniper: Quando premi L1 (mira), premi automaticamente anche L3 (hold breath)
    /// per avere massima precisione con sniper.
    ///
    /// Logica:
    /// - L1 premuto → L3 = true (hold breath)
    /// - L1 rilasciato → L3 torna normale
    /// - Durata: esattamente quanto tieni premuto L1
    /// </summary>
    public sealed class IsUsingSniperRule : IMacroRule
    {
        private readonly Func<RawInputState.Snapshot, bool> _activationCondition;

        public IsUsingSniperRule(Func<RawInputState.Snapshot, bool> activationCondition)
        {
            _activationCondition = activationCondition;
        }

        public void Apply(in RawInputState.Snapshot input, ref OutputState output)
        {
            // Se L1 è premuto, forza L3 = true
            if (_activationCondition(input))
            {
                output = output with { L3 = true };
            }
            // Altrimenti non fare nulla (lascia L3 com'è)
        }
    }
}