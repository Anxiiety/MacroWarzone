using Rug.Osc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using MacroWarzone.Macros;

namespace MacroWarzone;


    public sealed class OscInputReceiver : IDisposable
    {
        private readonly int _port;
        private readonly RawInputState _state;
        private Thread? _thread;
        private volatile bool _running;

        public OscInputReceiver(int port, RawInputState state)
        {
            _port = port;
            _state = state;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "OSC Receiver" };
            _thread.Start();
        }

        private void Loop()
        {
            const string Prefix = "/ds4windows/monitor/0/";

            try
            {
                using var receiver = new OscReceiver(_port);
                receiver.Connect();

                // local shadow
                byte lx = 128, ly = 128, rx = 128, ry = 128;
                byte l2 = 0, r2 = 0;
                bool l1 = false, r1 = false, tri = false, sq = false, cr = false, ci = false;
                bool du = false, dd = false, dl = false, dr = false, opt = false, sh = false, l3 = false, r3 = false, tc = false;

                while (_running)
                {
                    bool dirty = false;
                    bool gotAny = false;

                    while (receiver.TryReceive(out var packet))
                    {
                        gotAny = true;

                        if (packet is not OscMessage msg || msg.Count <= 0) continue;
                        if (msg[0] is not int iv) continue;

                        var addr = msg.Address;
                        if (!addr.StartsWith(Prefix, StringComparison.Ordinal)) continue;

                        ReadOnlySpan<char> suffix = addr.AsSpan(Prefix.Length);
                        byte b = (byte)Math.Clamp(iv, 0, 255);
                        bool pressed = iv > 0;

                        if (suffix.SequenceEqual("lx")) { lx = b; dirty = true; continue; }
                        if (suffix.SequenceEqual("ly")) { ly = b; dirty = true; continue; }
                        if (suffix.SequenceEqual("rx")) { rx = b; dirty = true; continue; }
                        if (suffix.SequenceEqual("ry")) { ry = b; dirty = true; continue; }

                        if (suffix.SequenceEqual("l2")) { l2 = b; dirty = true; continue; }
                        if (suffix.SequenceEqual("r2")) { r2 = b; dirty = true; continue; }

                        if (suffix.SequenceEqual("l1")) { l1 = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("r1")) { r1 = pressed; dirty = true; continue; }

                        if (suffix.SequenceEqual("triangle")) { tri = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("square")) { sq = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("cross")) { cr = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("circle")) { ci = pressed; dirty = true; continue; }

                        if (suffix.SequenceEqual("dpadup")) { du = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("dpaddown")) { dd = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("dpadleft")) { dl = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("dpadright")) { dr = pressed; dirty = true; continue; }

                        if (suffix.SequenceEqual("options")) { opt = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("share")) { sh = pressed; dirty = true; continue; }

                        if (suffix.SequenceEqual("l3")) { l3 = pressed; dirty = true; continue; }
                        if (suffix.SequenceEqual("r3")) { r3 = pressed; dirty = true; continue; }

                        // touch click se trovi l'address
                        // if (suffix.SequenceEqual("touchclick")) { tc = pressed; dirty = true; continue; }
                    }

                    if (dirty)
                    {
                        _state.ApplyBatch(s =>
                        {
                            s.SetLx(lx); s.SetLy(ly); s.SetRx(rx); s.SetRy(ry);
                            s.SetL2(l2); s.SetR2(r2);
                            s.SetL1(l1); s.SetR1(r1);
                            s.SetTriangle(tri); s.SetSquare(sq); s.SetCross(cr); s.SetCircle(ci);
                            s.SetDUp(du); s.SetDDown(dd); s.SetDLeft(dl); s.SetDRight(dr);
                            s.SetOptions(opt); s.SetShare(sh);
                            s.SetL3(l3); s.SetR3(r3);
                            s.SetTouchClick(tc);
                        });
                    }

                    if (!gotAny) Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("OSC receiver crashed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            _running = false;
        }
    }

