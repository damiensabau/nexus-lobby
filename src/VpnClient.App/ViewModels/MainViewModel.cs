using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VpnClient.App.Services;
using VpnClient.Core;

namespace VpnClient.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    // Dimensions logiques du graphique de débit (étiré par un Viewbox).
    private const int SpeedSamples = 60;
    private const double GraphWidth = 600;
    private const double GraphHeight = 100;

    private readonly ServiceClient _client = new();
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _pingTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Queue<double> _downSamples = new(Enumerable.Repeat(0d, SpeedSamples));
    private readonly Queue<double> _upSamples = new(Enumerable.Repeat(0d, SpeedSamples));
    private long _lastRx, _lastTx;
    private DateTime _lastSample = DateTime.UtcNow;
    private string? _editingProfileId;
    private string? _pendingImportPath;
    private int _lastMinute = -1;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private bool _hasStatus;       // premier état reçu : pas de message au lancement si déjà connecté
    private bool _wasConnected;    // pour le message « on te perd déjà »
    private string? _lastConnectedTitle;
    private bool _commandInFlight; // connexion / déconnexion envoyée : pas de rafraîchissement auto entre-temps
    private bool _userDisconnected = true;          // « Revenir à ma région » : pas de reconnexion automatique
    private readonly List<DateTime> _autoReconnects = []; // au plus 3 reconnexions auto en 10 minutes

    /// <summary>Préférences de l'utilisateur (%AppData%\NexusLobby\settings.json).</summary>
    public UserSettings Settings { get; }

    /// <summary>Notification Windows à afficher (zone de notification) : titre, message, succès ?</summary>
    public event Action<string, string, bool>? SystemNotification;

    public ObservableCollection<ServerItem> Servers { get; } = [];
    public ObservableCollection<MapMarker> Markers { get; } = [];

    /// <summary>Pays qui contiennent au moins un serveur, coloriés sur la carte.</summary>
    public ObservableCollection<CountryHighlight> CountryHighlights { get; } = [];

    /// <summary>Les 3 régions importées les mieux notées en ce moment.</summary>
    public ObservableCollection<ServerItem> Suggestions { get; } = [];
    public ICollectionView ServersView { get; }
    public IReadOnlyList<Location> AllLocations => Locations.All;

    public MainViewModel(UserSettings? settings = null)
    {
        Settings = settings ?? UserSettings.Load();
        _sortMode = Settings.SortMode;
        _suggestionsExpanded = Settings.SuggestionsExpanded;
        _minimizeToTray = Settings.MinimizeToTray;
        _notificationsEnabled = Settings.Notifications;
        _autoReconnect = Settings.AutoReconnect;
        _rememberCredentials = Settings.RememberCredentials;
        _startWithWindows = StartupRegistration.IsEnabled();
        StartupRegistration.RefreshPathIfEnabled();

        ServersView = CollectionViewSource.GetDefaultView(Servers);
        // Liste rangée en dossiers pays / ville ; les dossiers suivent l'ordre de leur meilleur serveur.
        ServersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServerItem.CountryGroup)));
        ServersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ServerItem.CityGroup)));
        ((ListCollectionView)ServersView).CustomSort = ServerDisplayComparer.Instance;
        ServersView.Filter = o => string.IsNullOrWhiteSpace(SearchText) ||
                                  ((ServerItem)o).SearchText.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

        _statusTimer.Tick += async (_, _) =>
        {
            // Les notes dépendent de l'heure : on les recalcule à chaque nouvelle minute.
            if (DateTime.UtcNow.Minute != _lastMinute) RecomputeSuggestions();
            if (!_commandInFlight) await RefreshStatusAsync();
        };
        _pingTimer.Tick += async (_, _) => await RefreshPingsAsync();
        _toastTimer.Tick += (_, _) => DismissToast();
    }

    public async Task InitializeAsync()
    {
        await LoadServersAsync();
        await RefreshStatusAsync();
        _statusTimer.Start();
        _pingTimer.Start();
        _ = RefreshPingsAsync();
        _ = RefreshVisibleCountryAsync();
        await ReconnectAtStartupAsync();
    }

    /// <summary>
    /// Connecté en quittant (ou PC éteint) et reconnexion auto activée : on revient sur la même région.
    /// Le service garde le tunnel quand l'app se ferme : rien à faire s'il est déjà actif.
    /// </summary>
    private async Task ReconnectAtStartupAsync()
    {
        if (!Settings.AutoReconnect || !IsIdle || Settings.LastConnectedProfileId is not { } id) return;
        if (Servers.FirstOrDefault(s => s.Profile.Id == id) is { IsExpired: false } server)
            await ConnectAsync(server);
    }

    // ───────────── État ─────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsBusy), nameof(IsIdle), nameof(BannerText),
        nameof(ProtectionTitle), nameof(MainActionLabel), nameof(NeedsCredentials), nameof(ShowsSavedCredentials),
        nameof(OverlayTitle), nameof(OverlayHint))]
    private ConnectionState _state = ConnectionState.Disconnected;

    // ───────────── Écran de chargement + messages ─────────────

    public string OverlayTitle => State == ConnectionState.Disconnecting
        ? "RETOUR À TA RÉGION"
        : $"CONNEXION À {(ActiveServer?.Title ?? "LA RÉGION").ToUpperInvariant()}";

    public string OverlayHint => State == ConnectionState.Disconnecting
        ? "On ferme le tunnel proprement…"
        : "Recherche de lobbies plus calmes…";

    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private bool _isToastSuccess;
    [ObservableProperty] private string _toastTitle = "";
    [ObservableProperty] private string _toastMessage = "";

    private void ShowToast(bool success, string title, string message)
    {
        IsToastSuccess = success;
        ToastTitle = title;
        ToastMessage = message;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastTimer.Stop();
        IsToastVisible = false;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProtectionTitle), nameof(OverlayTitle))]
    private ServerItem? _activeServer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsCredentials), nameof(ShowsSavedCredentials))]
    private ServerItem? _selectedServer;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Pays sous lequel Internet te voit (jamais l'IP ni la ville).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleCountryLine))]
    private string _visibleCountry = "…";

    public string VisibleCountryLine => $"Pays visible : {VisibleCountry}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuickConnectHint))]
    private ServerItem? _bestServer;

    public bool IsConnected => State == ConnectionState.Connected;
    public bool IsBusy => State is ConnectionState.Connecting or ConnectionState.Disconnecting;
    public bool IsIdle => State is ConnectionState.Disconnected or ConnectionState.Error;

    public string BannerText => State switch
    {
        ConnectionState.Connected => "RÉGION CHANGÉE",
        ConnectionState.Connecting => "CHANGEMENT…",
        ConnectionState.Disconnecting => "RETOUR…",
        ConnectionState.Error => "ERREUR",
        _ => "RÉGION D'ORIGINE",
    };

    public string ProtectionTitle => State switch
    {
        ConnectionState.Connected => $"Tu joues depuis {ActiveServer?.Title ?? "ta nouvelle région"}",
        ConnectionState.Connecting => $"Passage sur {ActiveServer?.Title ?? "la région"}…",
        ConnectionState.Disconnecting => "Retour à ta région…",
        ConnectionState.Error => "Connexion interrompue",
        _ => "Lobbies de ta région",
    };

    public string MainActionLabel => State switch
    {
        ConnectionState.Connected => "Revenir à ma région",
        ConnectionState.Connecting => "Annuler",
        ConnectionState.Disconnecting => "Retour…",
        _ => "Meilleur lobby maintenant",
    };

    public ServerItem? TopSuggestion => Suggestions.FirstOrDefault();

    public string QuickConnectHint => TopSuggestion is { } s
        ? $"Suggestion : {s.Title} · {s.LobbyScoreText}/100 · {s.LobbyGrade}"
        : "Ajoute des régions pour recevoir des suggestions";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingSuggestion))]
    private string? _missingSuggestion;

    public bool HasMissingSuggestion => MissingSuggestion is not null;

    [ObservableProperty]
    private bool _suggestionsExpanded = true;

    partial void OnSuggestionsExpandedChanged(bool value) => SaveSetting(s => s.SuggestionsExpanded = value);

    // ───────────── Réglages ─────────────

    [ObservableProperty] private bool _isSettingsPanelOpen;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsPanelOpen = !IsSettingsPanelOpen;

    [ObservableProperty] private bool _minimizeToTray;
    partial void OnMinimizeToTrayChanged(bool value) => SaveSetting(s => s.MinimizeToTray = value);

    [ObservableProperty] private bool _notificationsEnabled;
    partial void OnNotificationsEnabledChanged(bool value) => SaveSetting(s => s.Notifications = value);

    [ObservableProperty] private bool _autoReconnect;
    partial void OnAutoReconnectChanged(bool value) => SaveSetting(s => s.AutoReconnect = value);

    [ObservableProperty] private bool _rememberCredentials;
    partial void OnRememberCredentialsChanged(bool value) => SaveSetting(s => s.RememberCredentials = value);

    [ObservableProperty] private bool _startWithWindows;
    partial void OnStartWithWindowsChanged(bool value)
    {
        try
        {
            StartupRegistration.Set(value);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ShowError(VpnErrorCode.Unknown, $"Impossible de modifier le démarrage automatique : {ex.Message}");
        }
    }

    private void SaveSetting(Action<UserSettings> change)
    {
        change(Settings);
        Settings.Save();
    }
    public bool HasSuggestions => Suggestions.Count > 0;

    // ───────────── Session ─────────────

    [ObservableProperty] private string _duration = "0s";
    [ObservableProperty] private string _download = "0 o";
    [ObservableProperty] private string _upload = "0 o";
    [ObservableProperty] private string _downSpeed = "0 o/s";
    [ObservableProperty] private string _upSpeed = "0 o/s";
    [ObservableProperty] private string _speedScale = "0 o/s";
    [ObservableProperty] private PointCollection _downLine = [];
    [ObservableProperty] private PointCollection _downArea = [];
    [ObservableProperty] private PointCollection _upLine = [];

    // ───────────── Recherche, identifiants, erreurs ─────────────

    [ObservableProperty]
    private string _searchText = "";

    // ───────────── Tri de la liste ─────────────

    private string _displayOrder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    private SortMode _sortMode = SortMode.Score;

    public string SortLabel => SortMode switch
    {
        SortMode.Score => "Meilleur score",
        SortMode.Ping => "Meilleur ping",
        _ => "Pays A→Z",
    };

    partial void OnSortModeChanged(SortMode value)
    {
        SaveSetting(s => s.SortMode = value);
        ApplySort(force: true);
    }

    [RelayCommand]
    private void CycleSort() => SortMode = SortMode switch
    {
        SortMode.Score => SortMode.Ping,
        SortMode.Ping => SortMode.Name,
        _ => SortMode.Score,
    };

    /// <summary>Recalcule l'ordre ; ne reconstruit la liste que s'il a vraiment changé (pas de clignotement).</summary>
    private void ApplySort(bool force = false)
    {
        ServerDisplayComparer.ApplyKeys(Servers, SortMode);
        var order = string.Join("|", Servers.Order(ServerDisplayComparer.Instance).Select(s => s.Profile.Id));
        if (!force && order == _displayOrder) return;
        _displayOrder = order;

        var selected = SelectedServer;
        ServersView.Refresh();
        SelectedServer = selected; // la reconstruction peut perdre la sélection
    }

    partial void OnSearchTextChanged(string value) => ServersView.Refresh();

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";

    /// <summary>Champs identifiant / mot de passe à remplir (rien de mémorisé pour ce serveur).</summary>
    public bool NeedsCredentials => IsIdle && SelectedServer is { Profile.RequiresCredentials: true, HasSavedCredentials: false };

    /// <summary>Identifiants déjà mémorisés pour ce serveur : on affiche « mémorisés · Oublier ».</summary>
    public bool ShowsSavedCredentials => IsIdle && SelectedServer is { Profile.RequiresCredentials: true, HasSavedCredentials: true };

    [RelayCommand]
    private async Task ForgetCredentialsAsync()
    {
        if (SelectedServer is not { } server) return;
        var response = await _client.SendAsync(new IpcRequest { Type = RequestType.ForgetCredentials, ProfileId = server.Profile.Id });
        if (!response.Ok) ShowError(response.ErrorCode, response.Error);
        await LoadServersAsync();
    }

    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorTitle = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string? _errorActionLabel;
    private FriendlyError? _currentError;
    private ServerItem? _lastAttempt; // pour « Réessayer » / « Essayer une autre région »

    public bool HasErrorAction => _currentError is { Action: not ErrorAction.None };
    public string ServiceUnavailableTitle => ErrorCatalog.Describe(VpnErrorCode.ServiceUnavailable).Title;
    public string ServiceUnavailableMessage => ErrorCatalog.Describe(VpnErrorCode.ServiceUnavailable).Message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceUnavailable))]
    private bool _serviceAvailable = true;

    public bool ServiceUnavailable => !ServiceAvailable;

    // ───────────── Panneau d'emplacement (import / modification) ─────────────

    [ObservableProperty] private bool _isLocationPanelOpen;
    [ObservableProperty] private string _locationPanelTitle = "";
    [ObservableProperty] private string? _locationPanelFile;
    [ObservableProperty] private Location? _locationPanelChoice;

    /// <summary>Date d'expiration du compte (optionnelle) saisie dans le panneau.</summary>
    [ObservableProperty] private DateTime? _locationPanelExpiresAt;

    [RelayCommand]
    private void ClearExpiration() => LocationPanelExpiresAt = null;

    // ───────────── Chargement ─────────────

    private async Task LoadServersAsync()
    {
        var response = await _client.SendAsync(new IpcRequest { Type = RequestType.ListProfiles });
        if (!response.Ok)
        {
            ShowError(response.ErrorCode, response.Error);
            return;
        }

        var selectedId = SelectedServer?.Profile.Id;
        var pings = Servers.ToDictionary(s => s.Profile.Id, s => s.PingMs);

        Servers.Clear();
        foreach (var profile in response.Profiles ?? [])
        {
            Servers.Add(new ServerItem(profile) { PingMs = pings.GetValueOrDefault(profile.Id) });
        }

        AssignFolders(Servers);

        Markers.Clear();
        foreach (var group in Servers.Where(s => s.Location is not null).GroupBy(s => s.Location!))
        {
            Markers.Add(new MapMarker(group.Key, group.Count()));
        }

        CountryHighlights.Clear();
        foreach (var code in Servers.Where(s => s.Location is not null).Select(s => s.Location!.CountryCode).Distinct())
        {
            // Singapour et Hong Kong sont trop petits pour la carte 1:110m : pas de forme, juste le repère.
            if (Application.Current.TryFindResource($"Country.{code}") is Geometry geometry)
                CountryHighlights.Add(new CountryHighlight(code, geometry));
        }

        _displayOrder = ""; // nouvelle liste : reconstruction forcée par RecomputeSuggestions
        RecomputeSuggestions();
        SelectedServer = Servers.FirstOrDefault(s => s.Profile.Id == selectedId) ?? TopSuggestion ?? Servers.FirstOrDefault();
        UpdateHighlights(ActiveServer?.Profile.Id);
        UpdateBestServer();
    }

    /// <summary>
    /// Range les serveurs : dossier pays, sous-dossier ville seulement si le pays a plusieurs serveurs,
    /// et nom du fichier affiché quand plusieurs serveurs partagent la même ville.
    /// </summary>
    public static void AssignFolders(IEnumerable<ServerItem> servers)
    {
        foreach (var country in servers.GroupBy(s => s.CountryGroup))
        {
            var many = country.Count() > 1 && country.Key != CountryGroupKey.Unknown;
            foreach (var server in country)
            {
                server.CityGroup = many ? server.Location!.City : "";
                server.SharesCity = server.Location is not null && country.Count(s => s.Location == server.Location) > 1;
            }
        }
    }

    private async Task RefreshStatusAsync()
    {
        var response = await _client.SendAsync(new IpcRequest { Type = RequestType.GetStatus });
        ServiceAvailable = response.Ok;
        if (response.Status is not { } status) return;

        var previous = State;
        var running = status.State is ConnectionState.Connected or ConnectionState.Connecting or ConnectionState.Disconnecting;
        ActiveServer = running ? Servers.FirstOrDefault(s => s.Profile.Id == status.ProfileId) : null;
        State = status.State;
        StatusMessage = status.Message;
        UpdateHighlights(ActiveServer?.Profile.Id);
        UpdateSession(status);

        if (status.State == ConnectionState.Error && previous != ConnectionState.Error)
        {
            var failedServer = Servers.FirstOrDefault(s => s.Profile.Id == status.ProfileId);
            ShowError(status.ErrorCode, status.Message, failedServer);
            Notify(ErrorTitle, ErrorMessage, success: false);
            _ = HandleTunnelErrorAsync(status.ErrorCode, failedServer); // ne bloque pas le rafraîchissement (attente de 3 s)
        }
        if (status.State == ConnectionState.Connected) Password = "";
        AnnounceTransition(previous, status.State);

        // L'IP publique change à la connexion / déconnexion.
        if (previous != status.State && status.State is ConnectionState.Connected or ConnectionState.Disconnected)
        {
            _ = RefreshVisibleCountryAsync(delayMs: 1500);
        }
    }

    private async Task HandleTunnelErrorAsync(VpnErrorCode code, ServerItem? server)
    {
        if (server is null) return;

        // Identifiants mémorisés refusés : on les oublie, sinon chaque essai échouerait de la même façon.
        if (code == VpnErrorCode.AuthFailed && server.HasSavedCredentials)
        {
            await _client.SendAsync(new IpcRequest { Type = RequestType.ForgetCredentials, ProfileId = server.Profile.Id });
            await LoadServersAsync();
            return;
        }

        // Coupure en cours de partie : reconnexion automatique, avec une limite pour ne pas boucler.
        if (code is not (VpnErrorCode.TunnelLost or VpnErrorCode.Timeout) || !Settings.AutoReconnect || _userDisconnected) return;

        _autoReconnects.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(10));
        if (_autoReconnects.Count >= 3)
        {
            ErrorMessage += " Reconnexion automatique abandonnée après 3 essais.";
            return;
        }

        _autoReconnects.Add(DateTime.UtcNow);
        ErrorMessage += $" Reconnexion automatique dans 3 s ({_autoReconnects.Count}/3)…";
        await Task.Delay(TimeSpan.FromSeconds(3));
        if (State == ConnectionState.Error && !_userDisconnected && Servers.Contains(server)) await ConnectAsync(server);
    }

    private void Notify(string title, string message, bool success)
    {
        if (Settings.Notifications) SystemNotification?.Invoke(title, message, success);
    }

    private void AnnounceTransition(ConnectionState previous, ConnectionState current)
    {
        var firstStatus = !_hasStatus;
        _hasStatus = true;

        if (current == ConnectionState.Connected)
        {
            var arrived = previous != ConnectionState.Connected || _lastConnectedTitle != ActiveServer?.Title;
            _lastConnectedTitle = ActiveServer?.Title;
            _wasConnected = true;
            if (arrived) HasError = false; // l'ancienne erreur ne doit pas masquer le succès
            if (arrived && !firstStatus)
            {
                var score = ActiveServer?.Lobby is { } l ? $" · lobby {l.Value}/100 ({l.Grade.ToLowerInvariant()})" : "";
                var lag = ActiveServer?.Lobby is { PingWarning: true } && ActiveServer.PingMs is { } ms
                    ? $" Attention, ping élevé ({ms} ms) : ça peut laguer."
                    : "";
                ShowToast(true, "Tu es désormais connecté !",
                    $"Profite bien des bots à {_lastConnectedTitle ?? "ta nouvelle région"}{score}.{lag}");
                Notify("Tu es désormais connecté !", $"Région : {_lastConnectedTitle}{score}.{lag}", success: true);
                // Pas de remise à zéro des reconnexions auto ici : un serveur qui coupe toutes les 8 s bouclerait sans fin.
            }
            if (arrived && ActiveServer is { } connected && Settings.LastConnectedProfileId != connected.Profile.Id)
            {
                SaveSetting(s => s.LastConnectedProfileId = connected.Profile.Id);
            }
        }
        else if (current is ConnectionState.Disconnected or ConnectionState.Error)
        {
            if (_wasConnected && current == ConnectionState.Disconnected)
                ShowToast(false, "Mince, on te perd déjà…", $"Tu as quitté {_lastConnectedTitle ?? "le serveur"} : retour aux lobbies de ta région.");
            _wasConnected = false;
            _lastConnectedTitle = null;
        }
    }

    private void UpdateHighlights(string? activeId)
    {
        foreach (var s in Servers)
        {
            s.IsActive = s.Profile.Id == activeId;
            s.IsConnected = s.IsActive && State == ConnectionState.Connected;
        }

        foreach (var m in Markers)
        {
            m.IsActive = ActiveServer?.Location == m.Location;
            m.IsSelected = SelectedServer?.Location == m.Location;
        }

        foreach (var c in CountryHighlights)
        {
            c.IsActive = ActiveServer?.Location?.CountryCode == c.Code;
            c.IsSelected = SelectedServer?.Location?.CountryCode == c.Code;
        }
    }

    partial void OnSelectedServerChanged(ServerItem? value) => UpdateHighlights(ActiveServer?.Profile.Id);

    private void UpdateSession(TunnelStatus status)
    {
        // Hors connexion (erreur, déconnecté…), on affiche zéro même si le service garde ses derniers compteurs.
        if (status.State is not (ConnectionState.Connected or ConnectionState.Disconnecting))
        {
            status = new TunnelStatus { State = status.State };
        }

        var now = DateTime.UtcNow;
        var elapsed = Math.Max((now - _lastSample).TotalSeconds, 0.001);
        var down = status.BytesReceived >= _lastRx ? (status.BytesReceived - _lastRx) / elapsed : 0;
        var up = status.BytesSent >= _lastTx ? (status.BytesSent - _lastTx) / elapsed : 0;
        _lastRx = status.BytesReceived;
        _lastTx = status.BytesSent;
        _lastSample = now;

        _downSamples.Dequeue();
        _downSamples.Enqueue(down);
        _upSamples.Dequeue();
        _upSamples.Enqueue(up);

        Download = FormatBytes(status.BytesReceived);
        Upload = FormatBytes(status.BytesSent);
        DownSpeed = FormatBytes((long)down) + "/s";
        UpSpeed = FormatBytes((long)up) + "/s";
        Duration = status.ConnectedSince is { } since ? FormatDuration(now - since) : "0s";

        var max = Math.Max(10 * 1024, Math.Max(_downSamples.Max(), _upSamples.Max()) * 1.15);
        SpeedScale = FormatBytes((long)max) + "/s";
        DownLine = BuildLine(_downSamples, max);
        UpLine = BuildLine(_upSamples, max);
        var area = BuildLine(_downSamples, max);
        area.Add(new Point(GraphWidth, GraphHeight));
        area.Add(new Point(0, GraphHeight));
        DownArea = area;
    }

    private static PointCollection BuildLine(IEnumerable<double> samples, double max)
    {
        var points = new PointCollection(SpeedSamples);
        var i = 0;
        foreach (var v in samples)
        {
            points.Add(new Point(i * GraphWidth / (SpeedSamples - 1), GraphHeight - v / max * GraphHeight));
            i++;
        }
        return points;
    }

    private async Task RefreshPingsAsync()
    {
        await Task.WhenAll(Servers.ToList().Where(s => s.Profile.ServerHost is not null).Select(async s =>
        {
            var result = await NetworkProbe.ProbeAsync(s.Profile.ServerHost!, s.Profile.ServerPort);
            s.PingMethod = result?.Method;
            s.PingMs = result?.Ms;
        }));
        UpdateBestServer();
    }

    private void UpdateBestServer()
    {
        BestServer = Servers.Where(s => s.PingMs is not null).MinBy(s => s.PingMs) ?? Servers.FirstOrDefault();
        RecomputeSuggestions(); // le ping entre dans la note
    }

    private void RecomputeSuggestions()
    {
        _lastMinute = DateTime.UtcNow.Minute;
        foreach (var server in Servers) server.RefreshClock();
        ApplySort(); // les notes (et donc l'ordre) bougent avec l'heure et le ping

        var top = Servers.Where(s => s.Lobby is not null).OrderByDescending(s => s.LobbyValue).Take(3).ToList();
        if (!top.SequenceEqual(Suggestions))
        {
            Suggestions.Clear();
            foreach (var s in top) Suggestions.Add(s);
        }

        // Région idéale du moment qu'on n'a pas encore, si elle bat nettement la meilleure qu'on a.
        var missing = LobbyAdvisor.BestMissing(Servers.Select(s => s.Profile.LocationId), DateTime.UtcNow);
        MissingSuggestion = missing is { } m && m.Score.Value >= (TopSuggestion?.LobbyValue ?? 0) + 5
            ? $"Idée : un serveur à {m.Location.City} ({m.Location.Country}) noterait {m.Score.Value}/100 en ce moment · {m.Score.Reason}"
            : null;

        OnPropertyChanged(nameof(TopSuggestion));
        OnPropertyChanged(nameof(QuickConnectHint));
        OnPropertyChanged(nameof(HasSuggestions));
    }

    private async Task RefreshVisibleCountryAsync(int delayMs = 0)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        var code = await NetworkProbe.GetVisibleCountryAsync();
        VisibleCountry = code is null ? "inconnu (hors ligne ?)" : NetworkProbe.CountryName(code);
    }

    // ───────────── Commandes ─────────────

    [RelayCommand]
    private async Task MainActionAsync()
    {
        if (IsIdle)
        {
            var target = TopSuggestion ?? SelectedServer;
            if (target is null)
            {
                ShowError(VpnErrorCode.NoServers);
                return;
            }
            await ConnectAsync(target);
        }
        else
        {
            await DisconnectAsync();
        }
    }

    [RelayCommand]
    private async Task ConnectServerAsync(ServerItem? server)
    {
        if (server is null) return;
        if (server.IsActive && !IsIdle)
        {
            await DisconnectAsync();
            return;
        }
        await ConnectAsync(server);
    }

    [RelayCommand]
    private void SelectMarker(MapMarker? marker)
    {
        if (marker is null) return;
        SelectedServer = Servers.Where(s => s.Location == marker.Location).MinBy(s => s.PingMs ?? long.MaxValue);
    }

    private async Task ConnectAsync(ServerItem server)
    {
        HasError = false;
        SelectedServer = server;
        _lastAttempt = server;
        _userDisconnected = false;
        var typedCredentials = server.Profile.RequiresCredentials && Username.Length > 0 && Password.Length > 0;
        if (server.Profile.RequiresCredentials && !typedCredentials && !server.HasSavedCredentials)
        {
            ShowError(VpnErrorCode.CredentialsRequired, server: server);
            return;
        }

        // Affichage immédiat du chargement : le service peut d'abord fermer un tunnel déjà ouvert.
        DismissToast();
        ActiveServer = server;
        State = ConnectionState.Connecting;
        StatusMessage = "Préparation du tunnel…";

        IpcResponse response;
        _commandInFlight = true;
        try
        {
            response = await _client.SendAsync(new IpcRequest
            {
                Type = RequestType.Connect,
                ProfileId = server.Profile.Id,
                Username = typedCredentials ? Username : null,
                Password = typedCredentials ? Password : null,
                RememberCredentials = typedCredentials && RememberCredentials,
            });
        }
        finally
        {
            _commandInFlight = false;
        }
        if (!response.Ok)
        {
            ShowError(response.ErrorCode, response.Error, server);
            if (response.ErrorCode == VpnErrorCode.ProfileNotFound) await LoadServersAsync();
        }
        else if (typedCredentials && RememberCredentials)
        {
            await LoadServersAsync(); // affiche « identifiants mémorisés »
        }
        await RefreshStatusAsync();
    }

    private async Task DisconnectAsync()
    {
        HasError = false;
        _userDisconnected = true;
        SaveSetting(s => s.LastConnectedProfileId = null); // choix de l'utilisateur : pas de reconnexion au lancement
        // La requête ne répond qu'une fois le tunnel fermé : on affiche « Retour à ta région » tout de suite.
        DismissToast();
        State = ConnectionState.Disconnecting;
        StatusMessage = "Fermeture du tunnel…";

        IpcResponse response;
        _commandInFlight = true;
        try
        {
            response = await _client.SendAsync(new IpcRequest { Type = RequestType.Disconnect });
        }
        finally
        {
            _commandInFlight = false;
        }
        if (!response.Ok) ShowError(response.ErrorCode, response.Error);
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Ajouter une ou plusieurs régions",
            Filter = "Profils VPN (*.conf;*.ovpn)|*.conf;*.ovpn|WireGuard (*.conf)|*.conf|OpenVPN (*.ovpn)|*.ovpn",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;
        _ = ImportFilesAsync(dialog.FileNames);
    }

    public static bool IsProfileFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".conf" or ".ovpn";

    /// <summary>
    /// Un seul fichier : panneau pour choisir sa région. Plusieurs (sélection multiple ou glisser-déposer) :
    /// import direct avec la région devinée depuis le nom, puis un récapitulatif.
    /// </summary>
    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(IsProfileFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            ShowError(VpnErrorCode.UnsupportedFile);
            return;
        }

        if (files.Count == 1)
        {
            if (new FileInfo(files[0]).Length > MaxProfileSize)
            {
                ShowError(VpnErrorCode.FileTooLarge);
                return;
            }
            _pendingImportPath = files[0];
            _editingProfileId = null;
            LocationPanelTitle = "Dans quelle région est ce serveur ?";
            LocationPanelFile = Path.GetFileName(files[0]);
            LocationPanelChoice = Locations.Guess(files[0]);
            LocationPanelExpiresAt = null;
            IsLocationPanelOpen = true;
            return;
        }

        int imported = 0, withoutRegion = 0;
        (string File, IpcResponse Response)? firstFailure = null;
        foreach (var file in files)
        {
            var response = await ImportOneAsync(file, Locations.Guess(file)?.Id, expiresAt: null);
            if (response.Ok)
            {
                imported++;
                if (response.Profile?.LocationId is null) withoutRegion++;
            }
            else
            {
                firstFailure ??= (Path.GetFileName(file), response);
            }
        }

        await LoadServersAsync();
        _ = RefreshPingsAsync();

        if (firstFailure is { } failure)
        {
            ShowError(failure.Response.ErrorCode, failure.Response.Error);
            var others = files.Count - imported - 1;
            ErrorMessage = $"« {failure.File} » : {ErrorMessage}" + (others > 0 ? $" ({others} autre(s) fichier(s) refusé(s).)" : "")
                           + (imported > 0 ? $" {imported} région(s) ajoutée(s) quand même." : "");
            return;
        }

        ShowToast(true, $"{imported} régions ajoutées",
            withoutRegion == 0
                ? "Les régions ont été devinées depuis les noms des fichiers."
                : $"{withoutRegion} sans région reconnue : clic droit → « Modifier » pour la choisir.");
    }

    private const long MaxProfileSize = 1024 * 1024;

    private async Task<IpcResponse> ImportOneAsync(string path, string? locationId, DateTime? expiresAt)
    {
        string content;
        try
        {
            if (new FileInfo(path).Length > MaxProfileSize) return IpcResponse.Fail("Fichier trop gros.", VpnErrorCode.FileTooLarge);
            content = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IpcResponse.Fail($"Lecture impossible : {ex.Message}", VpnErrorCode.Unknown);
        }

        return await _client.SendAsync(new IpcRequest
        {
            Type = RequestType.ImportProfile,
            Name = Path.GetFileNameWithoutExtension(path),
            FileName = Path.GetFileName(path),
            Content = content,
            LocationId = locationId,
            ExpiresAt = expiresAt,
        });
    }

    // ── Glisser-déposer : indication visuelle pendant le survol ──

    [ObservableProperty] private bool _isDropTarget;

    [RelayCommand]
    private void ChangeLocation(ServerItem? server)
    {
        if (server is null) return;
        _pendingImportPath = null;
        _editingProfileId = server.Profile.Id;
        LocationPanelTitle = "Modifier ce serveur";
        LocationPanelFile = server.Profile.Name;
        LocationPanelChoice = server.Location;
        LocationPanelExpiresAt = server.ExpiresAt;
        IsLocationPanelOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmLocationAsync()
    {
        IsLocationPanelOpen = false;
        IpcResponse response;

        if (_editingProfileId is not null)
        {
            response = await _client.SendAsync(new IpcRequest
            {
                Type = RequestType.SetProfileLocation,
                ProfileId = _editingProfileId,
                LocationId = LocationPanelChoice?.Id,
            });
            if (response.Ok)
            {
                response = await _client.SendAsync(new IpcRequest
                {
                    Type = RequestType.SetProfileExpiration,
                    ProfileId = _editingProfileId,
                    ExpiresAt = LocationPanelExpiresAt,
                });
            }
        }
        else if (_pendingImportPath is not null)
        {
            response = await ImportOneAsync(_pendingImportPath, LocationPanelChoice?.Id, LocationPanelExpiresAt);
        }
        else
        {
            return;
        }

        if (!response.Ok)
        {
            ShowError(response.ErrorCode, response.Error);
            return;
        }

        await LoadServersAsync();
        SelectedServer = Servers.FirstOrDefault(s => s.Profile.Id == response.Profile?.Id) ?? SelectedServer;
        _ = RefreshPingsAsync();
    }

    [RelayCommand]
    private void CancelLocation() => IsLocationPanelOpen = false;

    [RelayCommand]
    private async Task DeleteServerAsync(ServerItem? server)
    {
        if (server is null) return;

        var confirm = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "Supprimer la région",
            Content = $"Supprimer « {server.Title} » ({server.Profile.Name}) ? Cette action est définitive.",
            PrimaryButtonText = "Supprimer",
            CloseButtonText = "Annuler",
        }.ShowDialogAsync();
        if (confirm != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

        var response = await _client.SendAsync(new IpcRequest { Type = RequestType.DeleteProfile, ProfileId = server.Profile.Id });
        if (!response.Ok) ShowError(response.ErrorCode, response.Error);
        await LoadServersAsync();
        await RefreshStatusAsync();
    }

    // ───────────── Utilitaires ─────────────

    private void ShowError(VpnErrorCode code, string? detail = null, ServerItem? server = null)
    {
        _currentError = ErrorCatalog.Describe(code, server?.Title, detail);
        ErrorTitle = _currentError.Title;
        ErrorMessage = _currentError.Message;
        ErrorActionLabel = _currentError.ActionLabel;
        OnPropertyChanged(nameof(HasErrorAction));
        DismissToast(); // l'erreur remplace le message en cours
        HasError = true;
    }

    [RelayCommand]
    private void DismissError() => HasError = false;

    [RelayCommand]
    private async Task ErrorActionAsync()
    {
        var error = _currentError;
        HasError = false;
        switch (error?.Action)
        {
            case ErrorAction.Retry when ServiceUnavailable:
                await LoadServersAsync();
                await RefreshStatusAsync();
                break;
            case ErrorAction.Retry:
                if (_lastAttempt is not null && Servers.Contains(_lastAttempt)) await ConnectAsync(_lastAttempt);
                else await MainActionAsync();
                break;
            case ErrorAction.PickAnother:
                var other = Servers.Where(s => s != _lastAttempt && s.Lobby is not null).MaxBy(s => s.LobbyValue);
                if (other is not null) await ConnectAsync(other);
                else ShowError(VpnErrorCode.NoServers);
                break;
            case ErrorAction.OpenUrl when error.Url is not null:
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(error.Url) { UseShellExecute = true });
                break;
            case ErrorAction.Import:
                Import();
                break;
            case ErrorAction.Credentials:
                if (_lastAttempt is not null) SelectedServer = _lastAttempt;
                break;
        }
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} o" : $"{value:0.0} {units[unit]}";
    }
}
