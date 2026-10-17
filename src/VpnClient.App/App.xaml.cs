using System.Windows;
using VpnClient.App.Services;

namespace VpnClient.App;

public partial class App : Application
{
    // Une seule instance par session Windows : relancer l'app (ou le démarrage auto) rouvre la fenêtre existante.
    private const string InstanceName = @"Local\NexusLobby.App";
    private const string ShowSignalName = @"Local\NexusLobby.App.Show";

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            // Déjà lancée : on lui demande de se montrer, puis on s'arrête.
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }
            Shutdown();
            return;
        }

        var window = new MainWindow();
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "NexusLobby.ShowSignal" };
        listener.Start();

        // Démarrage avec Windows : directement dans la zone de notification.
        if (e.Args.Contains(StartupRegistration.MinimizedArgument)) window.StartHidden();
        else window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        if (_instance is not null)
        {
            try { _instance.ReleaseMutex(); } catch (ApplicationException) { }
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
