using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VpnClient.App.Services;
using VpnClient.App.ViewModels;

namespace VpnClient.App;

public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _tray = new();
    private bool _initialized;
    private bool _exiting;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += async (_, _) => await EnsureInitializedAsync();

        // Zone de notification : état, menu et notifications Windows.
        _tray.OpenRequested += ShowFromTray;
        _tray.MainActionRequested += () => _viewModel.MainActionCommand.Execute(null);
        _tray.ExitRequested += ExitApplication;
        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.SystemNotification += (title, message, success) =>
        {
            // Fenêtre au premier plan : le message dans l'app suffit.
            if (!IsVisible || !IsActive || WindowState == WindowState.Minimized) _tray.Notify(title, message, success);
        };
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    // ───────────── Fenêtre / zone de notification ─────────────

    /// <summary>Démarrage avec Windows : pas de fenêtre, juste l'icône (le modèle se charge quand même).</summary>
    public void StartHidden() => _ = EnsureInitializedAsync();

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Fermer = rester dans la zone de notification (si activé), le tunnel continue.
        if (!_exiting && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_viewModel.Settings.TrayHintShown)
            {
                _tray.Notify("NEXUS LOBBY reste actif ici",
                    "Clique sur l'icône pour rouvrir la fenêtre. Clic droit → Quitter pour fermer l'app.", success: true);
                _viewModel.Settings.TrayHintShown = true;
                _viewModel.Settings.Save();
            }
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State) or nameof(MainViewModel.ProtectionTitle) or nameof(MainViewModel.MainActionLabel))
            _tray.Update(_viewModel.State, _viewModel.ProtectionTitle, _viewModel.MainActionLabel, canAct: !_viewModel.IsBusy);
    }

    // ───────────── Glisser-déposer de profils ─────────────

    private static string[] DroppedProfiles(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files ? files.Where(MainViewModel.IsProfileFile).ToArray() : [];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = DroppedProfiles(e).Length > 0;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        _viewModel.IsDropTarget = ok;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => _viewModel.IsDropTarget = false;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        _viewModel.IsDropTarget = false;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        if (files.Length > 0) await _viewModel.ImportFilesAsync(files);
    }

    // ───────────── Dossiers repliés (mémorisés dans les préférences) ─────────────

    /// <summary>Clé stable d'un dossier : « pays:FR » ou « ville:Paris ».</summary>
    public static string? FolderKey(object? groupName) => groupName switch
    {
        CountryGroupKey country => "pays:" + country.Code,
        string city when city.Length > 0 => "ville:" + city,
        _ => null,
    };

    private static string? FolderKeyOf(object sender) =>
        (sender as FrameworkElement)?.DataContext is CollectionViewGroup group ? FolderKey(group.Name) : null;

    private void OnFolderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && FolderKeyOf(expander) is { } key)
            expander.IsExpanded = !_viewModel.Settings.CollapsedFolders.Contains(key);
    }

    private void OnFolderToggled(object sender, RoutedEventArgs e)
    {
        // Expanded / Collapsed remontent depuis les dossiers enfants : on ne traite que le dossier concerné.
        // Un dossier tout juste créé s'ouvre par défaut avant d'être affiché : ce n'est pas un choix de l'utilisateur.
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not Expander { IsLoaded: true } expander
            || FolderKeyOf(sender) is not { } key)
            return;

        var changed = expander.IsExpanded
            ? _viewModel.Settings.CollapsedFolders.Remove(key)
            : _viewModel.Settings.CollapsedFolders.Add(key);
        if (changed) _viewModel.Settings.Save();
    }
}
