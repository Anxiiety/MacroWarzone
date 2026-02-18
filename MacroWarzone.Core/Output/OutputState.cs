namespace MacroWarzone;

public readonly record struct OutputState(
    double LeftX, double LeftY, double RightX, double RightY,
    byte L2, byte R2,
    bool L1, bool R1,
    bool Triangle, bool Square, bool Cross, bool Circle,
    bool DUp, bool DDown, bool DLeft, bool DRight,
    bool Options, bool Share,
    bool L3, bool R3,
    bool TouchClick
);
