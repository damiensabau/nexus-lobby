using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using VpnClient.Core;

namespace VpnClient.App.Services;

/// <summary>
/// Icône dans la zone de notification : couleur selon l'état, menu (ouvrir, meilleur lobby / revenir, quitter)
/// et notifications Windows. L'icône est dessinée à la volée : pas de fichier .ico à maintenir.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _mainAction;
    private ConnectionState? _drawnState;
    private IntPtr _iconHandle;

    public event Action? OpenRequested;
    public event Action? MainActionRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir NEXUS LOBBY", null, (_, _) => OpenRequested?.Invoke());
        _mainAction = new Forms.ToolStripMenuItem("Meilleur lobby maintenant", null, (_, _) => MainActionRequested?.Invoke());
        menu.Items.Add(_mainAction);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Text = "NEXUS LOBBY", Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
        Update(ConnectionState.Disconnected, "Lobbies de ta région", "Meilleur lobby maintenant", canAct: true);
    }

    /// <summary>Met à jour l'icône, l'infobulle et l'entrée principale du menu.</summary>
    public void Update(ConnectionState state, string title, string mainActionLabel, bool canAct)
    {
        // L'infobulle d'une icône de notification est limitée à 63 caractères.
        var tip = $"NEXUS LOBBY · {title}";
        _icon.Text = tip.Length > 63 ? tip[..62] + "…" : tip;
        _mainAction.Text = mainActionLabel;
        _mainAction.Enabled = canAct;

        if (_drawnState == state) return;
        _drawnState = state;
        var previous = _iconHandle;
        _icon.Icon = Draw(state, out _iconHandle);
        if (previous != IntPtr.Zero) DestroyIcon(previous);
    }

    public void Notify(string title, string message, bool success) =>
        _icon.ShowBalloonTip(5000, title, message, success ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);

    private static Icon Draw(ConnectionState state, out IntPtr handle)
    {
        var color = state switch
        {
            ConnectionState.Connected => Color.FromArgb(0x22, 0xC5, 0x5E),
            ConnectionState.Connecting or ConnectionState.Disconnecting => Color.FromArgb(0xF5, 0x9E, 0x0B),
            ConnectionState.Error => Color.FromArgb(0xEF, 0x44, 0x44),
            _ => Color.FromArgb(0x63, 0x66, 0xF1),
        };

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 30, 30);
            // Cible blanche : anneau + point central.
            using var ring = new Pen(Color.White, 3f);
            g.DrawEllipse(ring, 8, 8, 16, 16);
            g.FillEllipse(Brushes.White, 13, 13, 6, 6);
        }

        handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
