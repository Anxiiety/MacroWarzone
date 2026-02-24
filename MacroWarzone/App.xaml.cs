using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Resources;
using System.Windows.Threading;

using WpfApp = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace MacroWarzone;

public partial class App : WpfApp
{
    private const bool CloseToTrayEnabled = true;

    private NotifyIcon? _trayIcon;
    private bool _exitRequested;

    public static event Action? BeforeAppExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        SetupGlobalExceptionHandlers();
        SetupTrayIcon();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (MainWindow is null) return;

            MainWindow.Closing += MainWindow_Closing;
            MainWindow.StateChanged += MainWindow_StateChanged;
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SafeInvokeBeforeExit();
        DisposeTray();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _exitRequested = true;
        SafeInvokeBeforeExit();
        base.OnSessionEnding(e);
    }

    private void SetupGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                // Usa WPF MessageBox. Fine. Niente cast Frankenstein.
                WpfMessageBox.Show(
                    $"Errore critico:\n{args.Exception.Message}",
                    "Nexus Services",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                WpfMessageBox.Show(
                    $"Errore fatale:\n{ex?.Message ?? "Unknown error"}",
                    "Nexus Services",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
        };
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Text = "Nexus Services",
            Visible = true,
            Icon = LoadTrayIcon() ?? SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Apri", null, (_, __) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, __) => ExitFromTray());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, __) => ShowMainWindow();
    }

    private static Icon? LoadTrayIcon()
    {
        try
        {
            // Serve che Assets/Nexus12.ico sia Build Action = Resource
            var uri = new Uri("pack://application:,,,/Assets/Nexus12.ico", UriKind.Absolute);
            StreamResourceInfo? sri = GetResourceStream(uri);

            if (sri?.Stream is null)
                return null;

            // Copia su memoria: evita problemi di stream disposed / lazy read
            using var ms = new MemoryStream();
            sri.Stream.CopyTo(ms);
            ms.Position = 0;

            return new Icon(ms);
        }
        catch
        {
            return null;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (CloseToTrayEnabled && !_exitRequested)
        {
            e.Cancel = true;
            try { MainWindow?.Hide(); } catch { }
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { Shutdown(); } catch { }
        }));
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (!CloseToTrayEnabled) return;

        if (MainWindow is not null && MainWindow.WindowState == WindowState.Minimized)
        {
            try { MainWindow.Hide(); } catch { }
        }
    }

    private void ShowMainWindow()
    {
        try
        {
            if (MainWindow is null) return;

            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;

            // Focus “aggressivo” (Windows a volte fa lo snob)
            MainWindow.Activate();
            MainWindow.Topmost = true;
            MainWindow.Topmost = false;
            MainWindow.Focus();
        }
        catch { }
    }

    private void ExitFromTray()
    {
        _exitRequested = true;

        SafeInvokeBeforeExit();
        DisposeTray();

        try { MainWindow?.Close(); } catch { }

        try { Shutdown(); } catch { }
    }

    private void DisposeTray()
    {
        try
        {
            if (_trayIcon is null) return;

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        catch { }
    }

    private static void SafeInvokeBeforeExit()
    {
        try { BeforeAppExit?.Invoke(); }
        catch { }
    }
}