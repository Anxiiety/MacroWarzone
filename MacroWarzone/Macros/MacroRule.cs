
using System;
using System.Collections.Generic;
using System.Text;

namespace MacroWarzone.Macros;

    public interface IMacroRule
    {
        void Apply(in RawInputState.Snapshot input, ref OutputState output);
    }

