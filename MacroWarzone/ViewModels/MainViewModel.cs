using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using MacroWarzone.Services;
using MacroWarzone.Macros;
using CommunityToolkit.Mvvm.ComponentModel;
using MessageBox = System.Windows.Forms.MessageBox;

namespace MacroWarzone.ViewModels;

/// <summary>
/// MainViewModel con AUTO-SAVE automatico.
/// Ogni modifica → salva JSON + hot-reload macro (se backend running).
/// </summary>
public partial class MainViewModel : ObservableObject, INotifyPropertyChanged, IDisposable
{
    #region Private Fields

    private readonly BackendService _backend;
    private MacroConfiguration _draftConfig;

    private bool _isRunning;
    private string _statusMessage = "Pronto";
    [ObservableProperty]
    private bool _isDirty;

    private ObservableCollection<string> _logMessages = new();

    #endregion

    #region Properties - UI State

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<string> LogMessages
    {
        get => _logMessages;
        set
        {
            _logMessages = value;
            OnPropertyChanged();
        }
    }

    public bool CanStart => !IsRunning;
    public bool CanStop => IsRunning;

    public ObservableCollection<OutputTypeOption> OutputTypes { get; } = new()
{
    new OutputTypeOption("DualShock 4 (DS4)", GamepadOutputType.DualShock4),
    new OutputTypeOption("Xbox 360", GamepadOutputType.Xbox360),
};

    private OutputTypeOption _selectedOutputType;
    public OutputTypeOption SelectedOutputType
    {
        get => _selectedOutputType;
        set
        {
            if (_selectedOutputType == value) return;
            _selectedOutputType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDs4Selected));
            OnPropertyChanged(nameof(IsXboxSelected));
        }
    }
    public bool IsDs4Selected
    {
        get => SelectedOutputType?.Value == GamepadOutputType.DualShock4;
        set
        {
            if (!value) return;
            SelectedOutputType = OutputTypes.First(o => o.Value == GamepadOutputType.DualShock4);
        }
    }

    public bool IsXboxSelected
    {
        get => SelectedOutputType?.Value == GamepadOutputType.Xbox360;
        set
        {
            if (!value) return;
            SelectedOutputType = OutputTypes.First(o => o.Value == GamepadOutputType.Xbox360);
        }
    }




    #endregion

    // ✅ SOSTITUISCI LA REGION "Properties - Anti-Recoil" con questa:

    #region Properties - Anti-Recoil (con prefisso per matching XAML)

    public bool AntiRecoilEnabled
    {
        get => _draftConfig.AntiRecoil.Enabled;
        set
        {
            if (_draftConfig.AntiRecoil.Enabled != value)
            {
                _draftConfig.AntiRecoil.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: RecoilStrength → AntiRecoilStrength
    public double AntiRecoilStrength
    {
        get => _draftConfig.AntiRecoil.RecoilStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.5);
            if (Math.Abs(_draftConfig.AntiRecoil.RecoilStrength - clamped) > 0.001)
            {
                _draftConfig.AntiRecoil.RecoilStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: HorizontalBias → AntiRecoilHorizontalBias
    public double AntiRecoilHorizontalBias
    {
        get => _draftConfig.AntiRecoil.HorizontalBias;
        set
        {
            var clamped = Math.Clamp(value, -1.0, 1.0);
            if (Math.Abs(_draftConfig.AntiRecoil.HorizontalBias - clamped) > 0.001)
            {
                _draftConfig.AntiRecoil.HorizontalBias = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: VerticalBias → AntiRecoilVerticalBias
    public double AntiRecoilVerticalBias
    {
        get => _draftConfig.AntiRecoil.VerticalBias;
        set
        {
            var clamped = Math.Clamp(value, -1.0, 1.0);
            if (Math.Abs(_draftConfig.AntiRecoil.VerticalBias - clamped) > 0.001)
            {
                _draftConfig.AntiRecoil.VerticalBias = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: SmoothingTauMs → AntiRecoilSmoothingTauMs
    public double AntiRecoilSmoothingTauMs
    {
        get => _draftConfig.AntiRecoil.SmoothingTauMs;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 200.0);
            if (Math.Abs(_draftConfig.AntiRecoil.SmoothingTauMs - clamped) > 0.1)
            {
                _draftConfig.AntiRecoil.SmoothingTauMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: RampUpMs → AntiRecoilRampUpMs
    public int AntiRecoilRampUpMs
    {
        get => _draftConfig.AntiRecoil.RampUpMs;
        set
        {
            var clamped = Math.Clamp(value, 10, 500);
            if (_draftConfig.AntiRecoil.RampUpMs != clamped)
            {
                _draftConfig.AntiRecoil.RampUpMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // ✅ RINOMINATA: RampDownMs → AntiRecoilRampDownMs
    public int AntiRecoilRampDownMs
    {
        get => _draftConfig.AntiRecoil.RampDownMs;
        set
        {
            var clamped = Math.Clamp(value, 10, 500);
            if (_draftConfig.AntiRecoil.RampDownMs != clamped)
            {
                _draftConfig.AntiRecoil.RampDownMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    #endregion

    #region Properties - Aim Assist

    public bool AimAssistEnabled
    {
        get => _draftConfig.AimAssist.Enabled;
        set
        {
            if (_draftConfig.AimAssist.Enabled != value)
            {
                _draftConfig.AimAssist.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double RotationStrength
    {
        get => _draftConfig.AimAssist.RotationStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.RotationStrength - clamped) > 0.001)
            {
                _draftConfig.AimAssist.RotationStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double RotationConeAngle
    {
        get => _draftConfig.AimAssist.RotationConeAngle;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 90.0);
            if (Math.Abs(_draftConfig.AimAssist.RotationConeAngle - clamped) > 0.1)
            {
                _draftConfig.AimAssist.RotationConeAngle = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double StrafeThreshold
    {
        get => _draftConfig.AimAssist.StrafeThreshold;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.StrafeThreshold - clamped) > 0.001)
            {
                _draftConfig.AimAssist.StrafeThreshold = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double CameraThreshold
    {
        get => _draftConfig.AimAssist.CameraThreshold;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.CameraThreshold - clamped) > 0.001)
            {
                _draftConfig.AimAssist.CameraThreshold = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double TargetMotionThreshold
    {
        get => _draftConfig.AimAssist.TargetMotionThreshold;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.TargetMotionThreshold - clamped) > 0.001)
            {
                _draftConfig.AimAssist.TargetMotionThreshold = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double SlowdownRadius
    {
        get => _draftConfig.AimAssist.SlowdownRadius;
        set
        {
            var clamped = Math.Clamp(value, 0.05, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.SlowdownRadius - clamped) > 0.001)
            {
                _draftConfig.AimAssist.SlowdownRadius = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double SlowdownStrength
    {
        get => _draftConfig.AimAssist.SlowdownStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.SlowdownStrength - clamped) > 0.001)
            {
                _draftConfig.AimAssist.SlowdownStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double SlowdownSmoothMs
    {
        get => _draftConfig.AimAssist.SlowdownSmoothMs;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 200.0);
            if (Math.Abs(_draftConfig.AimAssist.SlowdownSmoothMs - clamped) > 0.1)
            {
                _draftConfig.AimAssist.SlowdownSmoothMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double DragStrength
    {
        get => _draftConfig.AimAssist.DragStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.DragStrength - clamped) > 0.001)
            {
                _draftConfig.AimAssist.DragStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double DragRadius
    {
        get => _draftConfig.AimAssist.DragRadius;
        set
        {
            var clamped = Math.Clamp(value, 0.05, 1.0);
            if (Math.Abs(_draftConfig.AimAssist.DragRadius - clamped) > 0.001)
            {
                _draftConfig.AimAssist.DragRadius = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double DragSmoothMs
    {
        get => _draftConfig.AimAssist.DragSmoothMs;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 200.0);
            if (Math.Abs(_draftConfig.AimAssist.DragSmoothMs - clamped) > 0.1)
            {
                _draftConfig.AimAssist.DragSmoothMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double AdsRotationMult
    {
        get => _draftConfig.AimAssist.AdsRotationMult;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 2.0);
            if (Math.Abs(_draftConfig.AimAssist.AdsRotationMult - clamped) > 0.001)
            {
                _draftConfig.AimAssist.AdsRotationMult = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double AdsDragMult
    {
        get => _draftConfig.AimAssist.AdsDragMult;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 2.0);
            if (Math.Abs(_draftConfig.AimAssist.AdsDragMult - clamped) > 0.001)
            {
                _draftConfig.AimAssist.AdsDragMult = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    #endregion

    // ✅ AGGIUNGI QUESTA REGIONE al tuo MainViewModel.cs
    // Inserisci dopo la region "Properties - Aim Assist"

    #region Properties - Cronus Zen Aim Assist

    public bool ZenCronusAimAssistEnabled
    {
        get => _draftConfig.ZenCronusAimAssist.Enabled;
        set
        {
            if (_draftConfig.ZenCronusAimAssist.Enabled != value)
            {
                _draftConfig.ZenCronusAimAssist.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // === STICKY BUBBLE ===
    public double BubbleRadius
    {
        get => _draftConfig.ZenCronusAimAssist.BubbleRadius;
        set
        {
            var clamped = Math.Clamp(value, 0.05, 0.5);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.BubbleRadius - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.BubbleRadius = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double BubbleStrength
    {
        get => _draftConfig.ZenCronusAimAssist.BubbleStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 0.95);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.BubbleStrength - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.BubbleStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double BubbleSmoothMs
    {
        get => _draftConfig.ZenCronusAimAssist.BubbleSmoothMs;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 200.0);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.BubbleSmoothMs - clamped) > 0.1)
            {
                _draftConfig.ZenCronusAimAssist.BubbleSmoothMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // === MICRO-CORRECTION ===
    public double MicroCorrectionRadius
    {
        get => _draftConfig.ZenCronusAimAssist.MicroCorrectionRadius;
        set
        {
            var clamped = Math.Clamp(value, 0.02, 0.3);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.MicroCorrectionRadius - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.MicroCorrectionRadius = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double MicroCorrectionStrength
    {
        get => _draftConfig.ZenCronusAimAssist.MicroCorrectionStrength;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 0.5);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.MicroCorrectionStrength - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.MicroCorrectionStrength = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double MicroCorrectionSmoothMs
    {
        get => _draftConfig.ZenCronusAimAssist.MicroCorrectionSmoothMs;
        set
        {
            var clamped = Math.Clamp(value, 5.0, 200.0);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.MicroCorrectionSmoothMs - clamped) > 0.1)
            {
                _draftConfig.ZenCronusAimAssist.MicroCorrectionSmoothMs = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // === SHAKE DAMPENING ===
    public double ShakeThreshold
    {
        get => _draftConfig.ZenCronusAimAssist.ShakeThreshold;
        set
        {
            var clamped = Math.Clamp(value, 0.5, 10.0);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.ShakeThreshold - clamped) > 0.1)
            {
                _draftConfig.ZenCronusAimAssist.ShakeThreshold = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double ShakeDampening
    {
        get => _draftConfig.ZenCronusAimAssist.ShakeDampening;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 0.95);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.ShakeDampening - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.ShakeDampening = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // === ADS BOOST ===
    public double ZenAdsMultiplier
    {
        get => _draftConfig.ZenCronusAimAssist.AdsMultiplier;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 3.0);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.AdsMultiplier - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.AdsMultiplier = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    // === RESPONSE CURVE ===
    public bool UseResponseOverride
    {
        get => _draftConfig.ZenCronusAimAssist.UseResponseOverride;
        set
        {
            if (_draftConfig.ZenCronusAimAssist.UseResponseOverride != value)
            {
                _draftConfig.ZenCronusAimAssist.UseResponseOverride = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public double ResponseCenterBoost
    {
        get => _draftConfig.ZenCronusAimAssist.ResponseCenterBoost;
        set
        {
            var clamped = Math.Clamp(value, 1.0, 2.0);
            if (Math.Abs(_draftConfig.ZenCronusAimAssist.ResponseCenterBoost - clamped) > 0.001)
            {
                _draftConfig.ZenCronusAimAssist.ResponseCenterBoost = clamped;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    #endregion

    #region Properties - Auto Ping, Sniper, Rapid Fire

    public bool AutoPingEnabled
    {
        get => _draftConfig.AutoPing.Enabled;
        set
        {
            if (_draftConfig.AutoPing.Enabled != value)
            {
                _draftConfig.AutoPing.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public bool IsUsingSniperEnabled
    {
        get => _draftConfig.IsUsingSniper.Enabled;
        set
        {
            if (_draftConfig.IsUsingSniper.Enabled != value)
            {
                _draftConfig.IsUsingSniper.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    public bool RapidFireEnabled
    {
        get => _draftConfig.RapidFire.Enabled;
        set
        {
            if (_draftConfig.RapidFire.Enabled != value)
            {
                _draftConfig.RapidFire.Enabled = value;
                OnPropertyChanged();
                AutoSaveAndReload();
            }
        }
    }

    #endregion

    #region Commands

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetToDefaultCommand { get; }

    public ICommand SaveCommand { get; }

    #endregion

    #region Constructor

    public MainViewModel()
    {
        _draftConfig = BackendService.LoadMacroConfiguration();
        AddLog("Configurazione caricata");

        _backend = new BackendService();
        _backend.StatusChanged += OnBackendStatusChanged;
        _backend.ErrorOccurred += OnBackendError;

        StartCommand = new RelayCommand(ExecuteStart, () => CanStart);
        StopCommand = new RelayCommand(ExecuteStop, () => CanStop);
        ResetToDefaultCommand = new RelayCommand(ExecuteResetToDefault);
        SelectedOutputType = OutputTypes.First(o => o.Value == GamepadOutputType.DualShock4);
        OnPropertyChanged(nameof(IsDs4Selected));
        OnPropertyChanged(nameof(IsXboxSelected));


        AddLog("Pronto per l'avvio");
    }

    #endregion

    #region Command Implementations

    private async void ExecuteStart()
    {
        try
        {
            AddLog("Avvio backend...");
            StatusMessage = "Avvio in corso...";

            // ✅ AUTO-SAVE prima dell'avvio (garantisce che il backend usi config più recente)
            
            
            BackendService.SaveMacroConfiguration(_draftConfig);
            
            
            _backend.OutputType = SelectedOutputType.Value;
            AddLog($"Output selezionato: {SelectedOutputType.Name}");


            _backend.OutputType = SelectedOutputType.Value;
            AddLog($"[DBG] OutputType -> {_backend.OutputType}");



            await _backend.StartAsync();

            IsRunning = true;
            StatusMessage = "✅ In esecuzione";
            AddLog("Backend avviato con successo");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Errore: {ex.Message}";
            AddLog($"ERRORE: {ex.Message}");

            MessageBox.Show(
                $"Impossibile avviare il backend:\n{ex.Message}",
                "Errore",
                (MessageBoxButtons)MessageBoxButton.OK,
                (MessageBoxIcon)MessageBoxImage.Error);
        }
    }

    private async void ExecuteStop()
    {
        try
        {
            AddLog("Arresto backend...");
            StatusMessage = "Arresto in corso...";

            await _backend.StopAsync();

            IsRunning = false;
            StatusMessage = "⏹️ Fermato";
            AddLog("Backend fermato");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Errore: {ex.Message}";
            AddLog($"ERRORE: {ex.Message}");
        }
    }

    private void ExecuteResetToDefault()
    {
        var result = MessageBox.Show(
            "Ripristinare la configurazione di default?\nTutte le modifiche personalizzate verranno perse.",
            "Conferma",
            (MessageBoxButtons)MessageBoxButton.YesNo,
            (MessageBoxIcon)MessageBoxImage.Question);

        if ((MessageBoxResult)result != MessageBoxResult.Yes)
            return;

        try
        {
            BackendService.ResetToDefaultConfiguration();
            _draftConfig = BackendService.LoadMacroConfiguration();

            // Notifica tutte le property cambiate
            OnPropertyChanged(string.Empty);

            AddLog("Configurazione ripristinata a default");
            StatusMessage = "Configurazione ripristinata";

            MessageBox.Show(
                "Configurazione ripristinata ai valori di default!",
                "Successo",
                (MessageBoxButtons)MessageBoxButton.OK,
                (MessageBoxIcon)MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddLog($"ERRORE reset: {ex.Message}");

            MessageBox.Show(
                $"Impossibile ripristinare la configurazione:\n{ex.Message}",
                "Errore",
                (MessageBoxButtons)MessageBoxButton.OK,
                (MessageBoxIcon)MessageBoxImage.Error);
        }
    }
    private void ExecuteSave()
    {
        try
        {
            // Salva su disco
            BackendService.SaveMacroConfiguration(_draftConfig);

            // Se backend sta girando, hot-reload macro
            if (IsRunning)
                _backend.SaveAndReloadMacros(_draftConfig);

            StatusMessage = "💾 Salvato";
            AddLog("Config salvata manualmente (SAVE).");
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Errore SAVE: {ex.Message}";
            AddLog($"ERRORE SAVE: {ex.Message}");
        }
    }


    #endregion

    #region Event Handlers

    private void OnBackendStatusChanged(object? sender, string status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = status;
            AddLog(status);
        });
    }

    private void OnBackendError(object? sender, Exception ex)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AddLog($"ERRORE BACKEND: {ex.Message}");
            StatusMessage = $"❌ Errore: {ex.Message}";
            IsRunning = false;

            MessageBox.Show(
                $"Errore critico del backend:\n{ex.Message}",
                "Errore Backend",
                (System.Windows.Forms.MessageBoxButtons)System.Windows.MessageBoxButton.OK,
                (System.Windows.Forms.MessageBoxIcon)System.Windows.MessageBoxImage.Error);
        });
    }

    #endregion

    #region Helper Methods

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";

        LogMessages.Add(logEntry);

        // Limita log a 100 entry (performance)
        while (LogMessages.Count > 100)
        {
            LogMessages.RemoveAt(0);
        }
    }

    /// <summary>
    /// ✅ AUTO-SAVE automatico + hot-reload macro.
    /// Chiamato ogni volta che modifichi un parametro dalla UI.
    /// 
    /// WORKFLOW:
    ///   1. Salva JSON su disco (macro_config_custom.json)
    ///   2. Se backend è running → hot-reload macro (nessun restart!)
    /// </summary>
    private void AutoSaveAndReload()
    {
        try
        {
            // 1. Salva config su disco
            BackendService.SaveMacroConfiguration(_draftConfig);

            // 2. Se backend running, ricarica macro senza restart
            if (IsRunning)
            {
                _backend.SaveAndReloadMacros(_draftConfig);
            }

            // Log silenzioso (no spam nel log visibile)
            // AddLog("Config salvata");  ← Commentato per non spammare
        }
        catch (Exception ex)
        {
            AddLog($"⚠️ ERRORE auto-save: {ex.Message}");
        }
    }

    public sealed class OutputTypeOption
    {
        public string Name { get; }
        public GamepadOutputType Value { get; }

        public OutputTypeOption(string name, GamepadOutputType value)
        {
            Name = name;
            Value = value;
        }
    }


    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _backend?.Dispose();
    }

    #endregion
}

#region RelayCommand

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

#endregion