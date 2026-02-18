using System;
using System.Collections.Generic;

namespace MacroWarzone;

/// <summary>
/// ConfigRoot aggiornata con supporto per MacroConfiguration
/// </summary>
public sealed class ConfigRoot
{
    public int OscPort { get; set; } = 9011;
    public int TickMs { get; set; } = 5;

    public string ActiveProfile { get; set; } = "Default";
    public Dictionary<string, GameProfile> Profiles { get; set; } = new();

    public GameProfile GetActiveProfile()
        => Profiles.TryGetValue(ActiveProfile, out var p) ? p : new GameProfile();
}