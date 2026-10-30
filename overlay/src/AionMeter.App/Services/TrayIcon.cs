using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AionMeter.App.Services;

/// <summary>Notification-area icon with the app menu. The meter keeps running while the overlay is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _clickThrough;
    private readonly ToolStripMenuItem _reset;
    private readonly ToolStripMenuItem _sendLog;
    private readonly ToolStripMenuItem _petRadar;
    private readonly ToolStripMenuItem _map;
    private readonly ToolStripMenuItem _checklist;
    private readonly ToolStripMenuItem _party;
    private readonly ToolStripMenuItem _miniMap;
    private readonly ToolStripMenuItem _history;
    private readonly ToolStripMenuItem _timers;
    private readonly ToolStripMenuItem _settings;
    private readonly ToolStripMenuItem _language;
    private readonly ToolStripMenuItem _demo;
    private readonly ToolStripMenuItem _replay;
    private readonly ToolStripMenuItem _exit;
    private readonly ToolStripMenuItem _update;
    private readonly ToolStripSeparator _updateSeparator;
    private readonly ToolStripMenuItem _checkUpdates;
    private readonly ToolStripMenuItem _site;     // 1 HP
    private readonly ToolStripMenuItem _netCheck; // 1 HP
    private readonly ToolStripMenuItem _analysis; // 1 HP
    private readonly ToolStripLabel _headMeter = Head();   // 1 HP: group headings
    private readonly ToolStripLabel _headWindows = Head();
    private readonly ToolStripMenuItem _tools = new();      // 1 HP: submenus
    private readonly ToolStripMenuItem _web = new();
    private bool _analysisRunning;
    private readonly Icon _image;
    private bool _overlayVisible = true;
    private string? _updateVersion;
    private Action? _balloonClick;

    public TrayIcon(TrayActions actions)
    {
        _image = CreateIcon();
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
        _update = new ToolStripMenuItem("", null, (_, _) => actions.Update()) { Visible = false };
        _update.Font = new Font(_update.Font, System.Drawing.FontStyle.Bold);
        _updateSeparator = new ToolStripSeparator { Visible = false };
        _checkUpdates = new ToolStripMenuItem("", null, (_, _) => actions.CheckUpdates());
        _toggle = new ToolStripMenuItem("", null, (_, _) => actions.ToggleOverlay());
        _clickThrough = new ToolStripMenuItem("", null, (_, _) => actions.ToggleClickThrough());
        _reset = new ToolStripMenuItem("", null, (_, _) => actions.Reset());
        _history = new ToolStripMenuItem("", null, (_, _) => actions.History());
        _timers = new ToolStripMenuItem("", null, (_, _) => actions.BossTimers());
        _petRadar = new ToolStripMenuItem("", null, (_, _) => actions.PetRadar());
        _map = new ToolStripMenuItem("", null, (_, _) => actions.Map());
        _checklist = new ToolStripMenuItem("", null, (_, _) => actions.Checklist());
        _party = new ToolStripMenuItem("", null, (_, _) => actions.Party());
        _miniMap = new ToolStripMenuItem("", null, (_, _) => actions.MiniMap());
        _settings = new ToolStripMenuItem("", null, (_, _) => actions.Settings());
        _language = new ToolStripMenuItem("", null, (_, _) => actions.ToggleLanguage());
        _demo = new ToolStripMenuItem("", null, (_, _) => actions.Demo());
        _replay = new ToolStripMenuItem("", null, (_, _) => actions.Replay());
        _exit = new ToolStripMenuItem("", null, (_, _) => actions.Exit());
        _site = new ToolStripMenuItem("", null, (_, _) => OpenUrl("https://guide.sulaslova.com/"));
        _netCheck = new ToolStripMenuItem("", null, (_, _) => OpenUrl("https://guide.sulaslova.com/#netcheck"));
        _analysis = new ToolStripMenuItem("", null, (_, _) => actions.Analysis());
        _sendLog = new ToolStripMenuItem("", null, (_, _) => actions.SendLog());
        // 1 HP: the menu in groups — the meter, the windows (ticked while open), tools and the site as submenus
        _tools.DropDownItems.AddRange([_analysis, _sendLog, new ToolStripSeparator(), _demo, _replay, new ToolStripSeparator(), _checkUpdates]);
        _web.DropDownItems.AddRange([_site, _netCheck]);
        menu.Items.AddRange([
            _update, _updateSeparator,
            _headMeter, _toggle, _clickThrough, _reset, new ToolStripSeparator(),
            _headWindows, _party, _history, _timers, _checklist, _map, _miniMap, _petRadar, new ToolStripSeparator(),
            _tools, _web, _settings, _language, new ToolStripSeparator(),
            _exit,
        ]);
        // ticks for what is open or on, read when the menu opens
        menu.Opening += (_, _) =>
        {
            if (actions.State?.Invoke() is not { } s) return;
            _party.Checked = s.Party;
            _petRadar.Checked = s.PetRadar;
            _miniMap.Checked = s.MiniMap;
            _clickThrough.Checked = s.ClickThrough;
        };
        ApplyTexts();

        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = AppInfo.Name,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) actions.ToggleOverlay();
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            var click = _balloonClick;
            _balloonClick = null;
            click?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonClick = null;
    }

    private static ToolStripLabel Head() => new()
    {
        ForeColor = Color.FromArgb(255, 128, 128, 128), Font = new Font("Segoe UI", 8f, System.Drawing.FontStyle.Bold), Margin = new Padding(6, 4, 0, 0),
    };

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Info("Open " + url + ": " + ex.Message); }
    }

    /// <summary>Menu wording in the current language.</summary>
    public void ApplyTexts()
    {
        var t = UiText.Current;
        _toggle.Text = _overlayVisible ? t.TrayHide : t.TrayShow;
        _clickThrough.Text = t.HkClick;
        _reset.Text = t.HkReset;
        _history.Text = t.TrayHistory;
        _timers.Text = t.TrayTimers;
        _petRadar.Text = t.PetRadarTray;
        _map.Text = t.MapTray;
        _checklist.Text = t.ChecklistTray;
        _party.Text = t.PartyTray;
        _miniMap.Text = t.MiniMapTray;
        _settings.Text = t.TraySettings;
        _language.Text = t.SwitchLanguage;
        _demo.Text = t.TrayDemo;
        _replay.Text = t.TrayReplay;
        _exit.Text = t.TrayExit;
        _checkUpdates.Text = t.TrayCheckUpdates;
        _site.Text = t.TraySite;
        _netCheck.Text = t.TrayNetCheck;
        _analysis.Text = _analysisRunning ? t.AnalysisTrayStop : t.AnalysisTrayStart;
        _sendLog.Text = t.SendLogTray;
        _headMeter.Text = t.TrayGroupMeter;
        _headWindows.Text = t.TrayGroupWindows;
        _tools.Text = t.TrayGroupTools;
        _web.Text = t.TrayGroupWeb;
        if (_updateVersion is not null) _update.Text = string.Format(t.TrayUpdate, _updateVersion);
    }

    /// <summary>1 HP: the analysis item says "stop and send" while a recording runs.</summary>
    public void SetAnalysis(bool running)
    {
        if (running == _analysisRunning) return;
        _analysisRunning = running;
        _analysis.Text = running ? UiText.Current.AnalysisTrayStop : UiText.Current.AnalysisTrayStart;
        _analysis.Checked = running;
    }

    /// <summary>"Update to 0.2.0…" at the top of the menu while a new version is available; null hides it.</summary>
    public void SetUpdate(string? version)
    {
        _updateVersion = version;
        _update.Visible = _updateSeparator.Visible = version is not null;
        if (version is not null) _update.Text = string.Format(UiText.Current.TrayUpdate, version);
    }

    public void Update(bool overlayVisible, bool clickThrough, string status)
    {
        _overlayVisible = overlayVisible;
        _toggle.Text = overlayVisible ? UiText.Current.TrayHide : UiText.Current.TrayShow;
        _clickThrough.Checked = clickThrough;
        var text = AppInfo.Name + " — " + status;
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    /// <param name="onClick">Runs when the notification itself is clicked (not when it times out or is closed).</param>
    public void ShowBalloon(string title, string text, Action? onClick = null)
    {
        _balloonClick = onClick;
        _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);
    }

    /// <summary>Gold "A" on a dark disc, drawn at runtime so the repo needs no binary assets.</summary>
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var bg = new SolidBrush(Color.FromArgb(255, 17, 23, 38));
            using var ring = new Pen(Color.FromArgb(255, 201, 164, 92), 2.2f);
            g.FillEllipse(bg, 1, 1, 30, 30);
            g.DrawEllipse(ring, 1.5f, 1.5f, 29, 29);
            using var font = new Font("Segoe UI", 15f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var gold = new SolidBrush(Color.FromArgb(255, 236, 203, 130));
            var size = g.MeasureString("A", font);
            g.DrawString("A", font, gold, (32 - size.Width) / 2 + 0.5f, (32 - size.Height) / 2 + 0.5f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

public sealed record TrayActions(
    Action ToggleOverlay,
    Action ToggleClickThrough,
    Action Reset,
    Action History,
    Action BossTimers,
    Action PetRadar,
    Action Map,
    Action Checklist,
    Action Party,
    Action MiniMap,
    Action Settings,
    Action Demo,
    Action Replay,
    Action ToggleLanguage,
    Action Update,
    Action CheckUpdates,
    Action Analysis,
    Action SendLog,
    Action Exit,
    Func<TrayState>? State = null);

/// <summary>1 HP: what the tray menu ticks — the windows open and the modes on.</summary>
public sealed record TrayState(bool Party, bool PetRadar, bool ClickThrough, bool MiniMap = false);
