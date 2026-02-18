using System.Windows;

namespace MacroWarzone.Views;

/// <summary>
/// Code-behind per MainWindow.
/// 
/// MVVM PATTERN:
/// - Codice minimo qui (solo inizializzazione)
/// - Tutta la logica in MainViewModel
/// - Nessun event handler nel code-behind
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // CLEANUP: Dispose del ViewModel quando chiudi la finestra
        Closing += (s, e) =>
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }
}