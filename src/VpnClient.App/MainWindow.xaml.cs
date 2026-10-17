using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VpnClient.App.ViewModels;

namespace VpnClient.App;

public partial class MainWindow
{
    // Dossiers (pays / ville) refermés par l'utilisateur : la liste est reconstruite à chaque import,
    // on remet donc chaque dossier dans l'état choisi.
    private readonly HashSet<string> _collapsedFolders = [];

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }

    private static string? FolderKey(object sender) =>
        (sender as FrameworkElement)?.DataContext is CollectionViewGroup group ? group.Name?.ToString() : null;

    private void OnFolderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && FolderKey(expander) is { } key)
            expander.IsExpanded = !_collapsedFolders.Contains(key);
    }

    private void OnFolderToggled(object sender, RoutedEventArgs e)
    {
        // Expanded / Collapsed remontent depuis les dossiers enfants : on ne traite que le dossier concerné.
        // Un dossier tout juste créé s'ouvre par défaut avant d'être affiché : ce n'est pas un choix de l'utilisateur.
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not Expander { IsLoaded: true } expander
            || FolderKey(sender) is not { } key)
            return;

        if (expander.IsExpanded) _collapsedFolders.Remove(key);
        else _collapsedFolders.Add(key);
    }
}
