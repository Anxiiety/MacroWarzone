using System;
using System.Windows;

namespace MacroWarzone;

/// <summary>
/// Entry point dell'applicazione WPF.
/// Sostituisce Program.cs della console app.
/// 
/// LIFECYCLE:
/// 1. OnStartup() viene chiamato quando l'app parte
/// 2. MainWindow viene mostrata automaticamente (StartupUri in XAML)
/// 3. OnExit() viene chiamato quando l'app chiude
/// </summary>
public partial class App : Application
{
    #region Application Lifecycle

    /// <summary>
    /// Chiamato all'avvio dell'applicazione.
    /// Equivalente al Main() di Program.cs.
    /// 
    /// DESIGN CHOICE:
    /// - Qui mettiamo inizializzazioni globali
    /// - Gestione eccezioni unhandled
    /// - Setup logging (se serve)
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        #region Global Exception Handler

        // THREADING: Cattura eccezioni da thread UI
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"Errore critico:\n{args.Exception.Message}",
                "GhostStick Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true; // Evita crash totale
        };

        // THREADING: Cattura eccezioni da thread background
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"Errore fatale:\n{ex?.Message ?? "Unknown error"}",
                "GhostStick Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        #endregion

        #region Verifiche iniziali (opzionale)

        // OPZIONALE: Verifica licenza qui invece che in MainViewModel
        // Così se fallisce, l'app non si apre proprio

        /*
        try
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyApp");
            var keyPath = Path.Combine(baseDir, "license.key");
            
            if (!File.Exists(keyPath))
            {
                MessageBox.Show("Licenza mancante!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            
            // ... validazione licenza
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore licenza: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        */

        #endregion
    }

    /// <summary>
    /// Chiamato quando l'applicazione si chiude.
    /// Equivalente al cleanup finale.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        // BEST PRACTICE: Cleanup risorse globali
        // (ViGEm, OSC, ecc. vengono gestiti da BackendService.Dispose)

        base.OnExit(e);
    }

    #endregion
}