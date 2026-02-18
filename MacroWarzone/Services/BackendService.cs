using MacroWarzone.Macros;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MacroWarzone.Services;

/// <summary>
/// BackendService con SAVE esplicito e SENZA weapon switching.
/// 
/// FLUSSO:
///   1. User modifica UI → MainViewModel._draftConfig cambia (volatile, non salvato)
///   2. User preme "SAVE" → MainViewModel chiama SaveAndReloadMacros()
///   3. Backend: salva JSON + ricostruisce macro + ricarica OutputLoop
/// 
/// WEAPON SWITCHING RIMOSSO:
///   - AntiRecoil usa SOLO un profilo fisso (quello in _draftConfig)
///   - Nessun Triangle switch
///   - Nessun event WeaponSwitched
/// </summary>
public class BackendService : IDisposable
{
    #region Events

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    #endregion

    #region File Paths

    private const string ProfilesPath = "profiles.json";
    private const string MacroDefaultPath = "macro_config.json";
    private const string MacroCustomPath = "macro_config_custom.json";

    #endregion

    // Developer note: per cambiare output modificare questo enum (es. Xbox360).
    public GamepadOutputType OutputType { get; set; } = GamepadOutputType.DualShock4;


    #region Fields

    private IGamepadOutput? _vigem;
    private OscInputReceiver? _osc;
    private OutputLoop? _loop;

    private Task? _loopTask;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private readonly object _lock = new();

    #endregion

    #region Properties

    public bool IsRunning { get { lock (_lock) return _isRunning; } }

    #endregion

    #region Lifecycle

    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (_isRunning)
                throw new InvalidOperationException("Backend già in esecuzione");
        }

        try
        {
            RaiseStatus("Inizializzazione file di configurazione...");

            var (cfg, profile, macroConfig) = await Task.Run(() =>
            {
                EnsureAllFilesExist();

                var configRoot = LoadProfilesSafe();
                var gameProfile = configRoot.GetActiveProfile();
                var macro = LoadMacroConfiguration();

                return (configRoot, gameProfile, macro);
            });

            RaiseStatus($"Inizializzazione ViGEm ({OutputType})...");
            _vigem = GamepadOutputFactory.Create(OutputType);

            _vigem.Connect();

            RaiseStatus($"Avvio OSC su porta {cfg.OscPort}...");
            var raw = new RawInputState();
            _osc = new OscInputReceiver(cfg.OscPort, raw);
            _osc.Start();

            RaiseStatus("Creazione pipeline macro...");
            double sampleRateHz = 1000.0 / cfg.TickMs;

            var hipLeft = new StickProcessor(profile.Hip.Left, sampleRateHz);
            var hipRight = new StickProcessor(profile.Hip.Right, sampleRateHz);
            var adsLeft = new StickProcessor(profile.Ads.Left, sampleRateHz);
            var adsRight = new StickProcessor(profile.Ads.Right, sampleRateHz);

            // ✅ Build macro dalla config caricata
            var macros = MacroEngine.BuildRulesFromConfig(macroConfig);

            _loop = new OutputLoop(cfg, profile, raw, _vigem,
                hipLeft, hipRight, adsLeft, adsRight, macros, macroConfig);

            // ❌ NESSUN WeaponSwitched event (rimosso)

            RaiseStatus("Avvio loop realtime...");
            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() =>
            {
                try { _loop.Run(); }
                catch (Exception ex) { RaiseError(ex); }
            }, _cts.Token);

            lock (_lock) _isRunning = true;
            RaiseStatus("✅ Backend attivo");
        }
        catch (Exception ex)
        {
            await StopAsync();
            RaiseError(ex);
            throw;
        }
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
        }

        try
        {
            RaiseStatus("Arresto in corso...");

            _loop?.Stop();

            if (_loopTask != null)
            {
                var completed = await Task.WhenAny(_loopTask, Task.Delay(2000));
                if (completed != _loopTask)
                    _cts?.Cancel();
            }

            _osc?.Dispose();
            _vigem?.Dispose();

            _loopTask = null;
            _cts?.Dispose();
            _cts = null;
            _loop = null;

            lock (_lock) _isRunning = false;
            RaiseStatus("Backend fermato");
        }
        catch (Exception ex)
        {
            RaiseError(ex);
            throw;
        }
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    #endregion

    #region Save + Reload (chiamato dal pulsante "SAVE" in UI)

    /// <summary>
    /// CHIAMATO DAL PULSANTE "SAVE" nella UI.
    /// 
    /// WORKFLOW:
    ///   1. Serializza config → macro_config_custom.json (disco)
    ///   2. Rebuild macro con nuovi valori
    ///   3. OutputLoop.ReloadMacros() (atomico, nessun restart necessario)
    /// 
    /// THREAD SAFETY:
    ///   - Chiamato da UI thread
    ///   - OutputLoop riceve nuove macro atomicamente (swap pointer)
    /// </summary>
    public void SaveAndReloadMacros(MacroConfiguration draftConfig)
    {
        try
        {
            RaiseStatus("Salvataggio configurazione...");

            // 1. Valida config
            EnsureConfigInvariant(draftConfig);

            // 2. Salva su disco (macro_config_custom.json)
            SaveMacroConfiguration(draftConfig);

            // 3. Se il backend è running, ricarica le macro HOT
            if (IsRunning && _loop != null)
            {
                var newMacros = MacroEngine.BuildRulesFromConfig(draftConfig);
                _loop.ReloadMacros(newMacros);
                RaiseStatus("✅ Macro ricaricate (hot-reload)");
            }
            else
            {
                RaiseStatus("✅ Configurazione salvata (ricaricata al prossimo Start)");
            }
        }
        catch (Exception ex)
        {
            RaiseError(ex);
            throw;
        }
    }

    #endregion

    #region File Bootstrap

    private static void EnsureAllFilesExist()
    {
        if (!File.Exists(ProfilesPath))
        {
            Debug.WriteLine($"[BOOTSTRAP] {ProfilesPath} non trovato → creazione default");
            WriteProfilesDefault();
        }

        if (!File.Exists(MacroDefaultPath))
        {
            Debug.WriteLine($"[BOOTSTRAP] {MacroDefaultPath} non trovato → creazione default");
            WriteMacroDefault(MacroDefaultPath);
        }
    }

    #endregion

    #region Profiles.json Load

    private static ConfigRoot LoadProfilesSafe()
    {
        if (!File.Exists(ProfilesPath))
        {
            Debug.WriteLine($"[LOAD] {ProfilesPath} mancante, creazione default");
            WriteProfilesDefault();
        }

        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var options = MakeJsonOptions();
            var cfg = JsonSerializer.Deserialize<ConfigRoot>(json, options);

            if (cfg == null)
                throw new InvalidDataException("Deserializzazione ha prodotto null");

            if (cfg.Profiles == null || cfg.Profiles.Count == 0)
                throw new InvalidDataException("Nessun profilo trovato in profiles.json");

            return cfg;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RECOVERY] profiles.json corrotto: {ex.Message}");
            Debug.WriteLine($"[RECOVERY] Sovrascrittura con default (backup in .bak)");

            try { File.Copy(ProfilesPath, ProfilesPath + ".bak", overwrite: true); }
            catch { }

            WriteProfilesDefault();

            var json = File.ReadAllText(ProfilesPath);
            return JsonSerializer.Deserialize<ConfigRoot>(json, MakeJsonOptions())
                   ?? throw new InvalidOperationException("Impossibile creare ConfigRoot di default");
        }
    }

    #endregion

    #region Macro Config Load

    public static MacroConfiguration LoadMacroConfiguration()
    {
        var options = MakeJsonOptions();

        // Priorità 1: custom
        if (File.Exists(MacroCustomPath))
        {
            try
            {
                var json = File.ReadAllText(MacroCustomPath);
                var config = JsonSerializer.Deserialize<MacroConfiguration>(json, options);

                if (config != null)
                {
                    EnsureConfigInvariant(config);
                    Debug.WriteLine($"[LOAD] Config caricata da {MacroCustomPath}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] {MacroCustomPath} corrotto ({ex.Message}) → uso default");
            }
        }

        // Priorità 2: default
        if (File.Exists(MacroDefaultPath))
        {
            try
            {
                var json = File.ReadAllText(MacroDefaultPath);
                var config = JsonSerializer.Deserialize<MacroConfiguration>(json, options);

                if (config != null)
                {
                    EnsureConfigInvariant(config);
                    Debug.WriteLine($"[LOAD] Config caricata da {MacroDefaultPath}");
                    return config;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARNING] {MacroDefaultPath} corrotto ({ex.Message}) → rigenero");
            }
        }

        // Fallback: crea da zero
        Debug.WriteLine("[BOOTSTRAP] Nessun file macro trovato → creazione da zero");
        var freshConfig = CreateFreshMacroConfig();
        EnsureConfigInvariant(freshConfig);

        try { WriteMacroDefault(MacroDefaultPath); } catch { }

        return freshConfig;
    }

    #endregion

    #region Macro Config Save

    public static void SaveMacroConfiguration(MacroConfiguration config)
    {
        try
        {
            EnsureConfigInvariant(config);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(config, options);
            AtomicWrite(MacroCustomPath, json);
            Debug.WriteLine($"[SAVE] Config salvata in {MacroCustomPath}");
        }
        catch (Exception ex)
        {
            throw new IOException($"Impossibile salvare configurazione: {ex.Message}", ex);
        }
    }

    public static void ResetToDefaultConfiguration()
    {
        if (File.Exists(MacroCustomPath))
        {
            File.Delete(MacroCustomPath);
            Debug.WriteLine($"[RESET] {MacroCustomPath} eliminato → prossimo avvio usa default");
        }
    }

    #endregion

    #region JSON Writers

    private static void WriteProfilesDefault()
    {
        var root = new ConfigRoot
        {
            OscPort = 9011,
            TickMs = 5,
            ActiveProfile = "Default",
            Profiles = new Dictionary<string, GameProfile>
            {
                ["Default"] = new GameProfile
                {
                    Hip = new ResponseSet
                    {
                        Left = new AxisConfig { Deadzone = 0.05, Expo = 0.0 },
                        Right = new AxisConfig { Deadzone = 0.05, Expo = 0.10 }
                    },
                    Ads = new ResponseSet
                    {
                        Left = new AxisConfig { Deadzone = 0.04, Expo = 0.0 },
                        Right = new AxisConfig { Deadzone = 0.04, Expo = 0.15 }
                    },
                    Activation = new ActivationConfig
                    {
                        AdsWhenR2Above = 20,
                        MinIntentMagnitude = 0.02,
                        TriggerNoiseThreshold = 3
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(ProfilesPath, json);
        Debug.WriteLine($"[WRITE] {ProfilesPath} scritto");
    }

    private static void WriteMacroDefault(string path)
    {
        var config = CreateFreshMacroConfig();
        EnsureConfigInvariant(config);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(path, json);
        Debug.WriteLine($"[WRITE] {path} scritto");
    }

    /// <summary>
    /// Config di fabbrica SENZA weapon profiles (usa single recoil profile).
    /// </summary>
    private static MacroConfiguration CreateFreshMacroConfig()
    {
        return new MacroConfiguration
        {
            AntiRecoil = new AntiRecoilConfig
            {
                Enabled = true,
                Trigger = "R1",

                // ✅ SINGLE WEAPON PROFILE (non array)
                RecoilStrength = 0.24,
                HorizontalBias = 0.0,
                VerticalBias = -1.0,
                SmoothingTauMs = 25,
                RampUpMs = 120,
                RampDownMs = 80
            },
            AimAssist = new AimAssistConfig
            {
                Enabled = true,
                ActivationTrigger = "L1+R1",
                ADSTrigger = "L1",
                RotationStrength = 0.20,
                RotationConeAngle = 35.0,
                StrafeThreshold = 0.05,
                CameraThreshold = 0.08,
                TargetMotionThreshold = 0.05,
                SlowdownRadius = 0.25,
                SlowdownStrength = 0.65,
                SlowdownSmoothMs = 40,
                DragStrength = 0.30,
                DragRadius = 0.20,
                DragSmoothMs = 35,
                AdsRotationMult = 0.7,
                AdsDragMult = 1.6
            },
            ZenCronusAimAssist = new ZenCronusAimAssistConfig
            {
                Enabled = false,
                ActivationTrigger = "L1+R1",
                ADSTrigger = "L1",
                BubbleRadius = 0.20, //Raggio zona slowdown (20% schermo)
                BubbleStrength = 0.85,//Forza slowdown (85% riduzione sens)
                BubbleSmoothMs = 25,
                MicroCorrectionRadius = 0.10, // Raggio zona pull magnetico
                MicroCorrectionStrength = 0.20,  //Forza pull verso centro
                MicroCorrectionSmoothMs = 20,
                ShakeThreshold = 3.5, //Velocità per rilevare tremori (rad/s)
                ShakeDampening = 0.70, //Quanto sopprimere tremori (70%)
                AdsMultiplier = 1.5, //Boost in ADS (+50%)
                UseResponseOverride = true,
                ResponseCenterBoost = 1.3 // Boost sensibilità centro (+30%
            },
            AutoPing = new AutoPingConfig
            {
                Enabled = false,
                Trigger = "L1+R1",
                PingDurationMs = 100
            },
            IsUsingSniper = new IsUsingSniperConfig
            {
                Enabled = false,
                Trigger = "L1"
            },
            RapidFire = new RapidFireConfig
            {
                Enabled = false,
                Trigger = "R1",
                FireRateHz = 15
            }
        };
    }

    #endregion

    #region Helpers

    public static void EnsureConfigInvariant(MacroConfiguration cfg)
    {
        cfg.AntiRecoil ??= new AntiRecoilConfig();
        cfg.AimAssist ??= new AimAssistConfig();
        cfg.ZenCronusAimAssist ??= new ZenCronusAimAssistConfig();
        cfg.AutoPing ??= new AutoPingConfig();
        cfg.IsUsingSniper ??= new IsUsingSniperConfig();
        cfg.RapidFire ??= new RapidFireConfig();
    }

    private static JsonSerializerOptions MakeJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static void AtomicWrite(string path, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var bak = path + ".bak";

        File.WriteAllText(tmp, contents);

        using (var fs = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            fs.Flush(true);

        if (File.Exists(path))
        {
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            try { File.Delete(bak); } catch { }
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    private void RaiseStatus(string msg) => StatusChanged?.Invoke(this, msg);
    private void RaiseError(Exception ex) => ErrorOccurred?.Invoke(this, ex);

    #endregion
}