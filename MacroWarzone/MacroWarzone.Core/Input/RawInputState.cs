using System;

namespace MacroWarzone;

public sealed class RawInputState
{
    private readonly object _lock = new();

    public byte Lx { get; private set; } = 128;
    public byte Ly { get; private set; } = 128;
    public byte Rx { get; private set; } = 128;
    public byte Ry { get; private set; } = 128;

    public byte L2 { get; private set; } = 0;
    public byte R2 { get; private set; } = 0;

    public bool L1 { get; private set; }
    public bool R1 { get; private set; }

    public bool Triangle { get; private set; }
    public bool Square { get; private set; }
    public bool Cross { get; private set; }
    public bool Circle { get; private set; }

    public bool DUp { get; private set; }
    public bool DDown { get; private set; }
    public bool DLeft { get; private set; }
    public bool DRight { get; private set; }

    public bool Options { get; private set; }
    public bool Share { get; private set; }

    public bool L3 { get; private set; }
    public bool R3 { get; private set; }

    public bool TouchClick { get; private set; }

    public void ApplyBatch(Action<RawInputState> update)
    {
        lock (_lock) update(this);
    }

    public Snapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new Snapshot(
                Lx, Ly, Rx, Ry,
                L2, R2,
                L1, R1,
                Triangle, Square, Cross, Circle,
                DUp, DDown, DLeft, DRight,
                Options, Share,
                L3, R3,
                TouchClick
            );
        }
    }

    public readonly record struct Snapshot(
        byte Lx, byte Ly, byte Rx, byte Ry,
        byte L2, byte R2,
        bool L1, bool R1,
        bool Triangle, bool Square, bool Cross, bool Circle,
        bool DUp, bool DDown, bool DLeft, bool DRight,
        bool Options, bool Share,
        bool L3, bool R3,
        bool TouchClick
    );

    // Setters usati dal receiver
    internal void SetLx(byte v) => Lx = v;
    internal void SetLy(byte v) => Ly = v;
    internal void SetRx(byte v) => Rx = v;
    internal void SetRy(byte v) => Ry = v;

    internal void SetL2(byte v) => L2 = v;
    internal void SetR2(byte v) => R2 = v;

    internal void SetL1(bool v) => L1 = v;
    internal void SetR1(bool v) => R1 = v;

    internal void SetTriangle(bool v) => Triangle = v;
    internal void SetSquare(bool v) => Square = v;
    internal void SetCross(bool v) => Cross = v;
    internal void SetCircle(bool v) => Circle = v;

    internal void SetDUp(bool v) => DUp = v;
    internal void SetDDown(bool v) => DDown = v;
    internal void SetDLeft(bool v) => DLeft = v;
    internal void SetDRight(bool v) => DRight = v;

    internal void SetOptions(bool v) => Options = v;
    internal void SetShare(bool v) => Share = v;

    internal void SetL3(bool v) => L3 = v;
    internal void SetR3(bool v) => R3 = v;

    internal void SetTouchClick(bool v) => TouchClick = v;
}
