using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaxWindowsTweaks.Core;

namespace PaxWindowsTweaks.Views;

/// <summary>
/// The whole app: a sidebar of pages (Tweaks, Preferences, Debloat, Install apps, Features &amp; fixes, Windows Update,
/// Network &amp; power, Log) and a status bar that shows what's running. Built in code, PAX neon theme.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static IBrush Brush(string c) => new SolidColorBrush(Color.Parse(c));
    private static readonly IBrush Muted = Brush("#8A92A6");
    private static readonly IBrush Good = Brush("#19F58C");
    private static readonly IBrush Info = Brush("#00C8FF");
    private static readonly IBrush Warn = Brush("#F5B14C");
    private static readonly IBrush Bad = Brush("#F2666A");
    private static readonly IBrush TextB = Brush("#E8EAF0");
    private static readonly IBrush Sidebar = Brush("#0B0E13");

    public static readonly string[] PageNames = ["Tweaks", "Preferences", "Debloat", "Install apps", "Features & fixes", "Windows Update", "Network & power", "Log"];
    private static readonly string[] PageGlyphs = ["⚙", "◐", "✂", "⇩", "✚", "⟳", "⌁", "≡"];

    private readonly bool _elevated;
    private readonly Dictionary<string, Control> _pages = [];
    private readonly Dictionary<string, Button> _nav = [];
    private readonly Border _pageHost = new();
    private readonly Border _notice = new() { Classes = { "notice" }, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _noticeText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _noticeAction = new() { Classes = { "primary" }, MinHeight = 30, Padding = new Thickness(12, 2), Margin = new Thickness(10, 0, 0, 0), IsVisible = false };
    private Func<Task>? _noticeRun;
    private readonly TextBlock _status = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Width = 160, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _cancel = new() { Content = "Stop", Classes = { "ghost" }, MinHeight = 28, Padding = new Thickness(10, 2), IsVisible = false };
    private readonly StringBuilder _log = new();
    private readonly TextBox _logBox = new() { Classes = { "mono" }, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top };
    private CancellationTokenSource? _jobCts;
    private bool _busy;
    private bool _refreshing;
    private string _page = "Tweaks";

    public string CurrentPage => _page;
    public bool IsBusy => _busy;

    public MainWindow() : this(elevated: null, autoLoad: true)
    {
    }

    /// <param name="elevated">Override the elevation state (screenshots).</param>
    /// <param name="autoLoad">Read states, installed apps and so on when the window opens (the self-test does it itself).</param>
    public MainWindow(bool? elevated, bool autoLoad)
    {
        _elevated = elevated ?? Elevation.IsElevated;
        Title = _elevated ? "PAX Windows Tweaks (Administrator)" : "PAX Windows Tweaks";
        Icon = BrandIcon.Window();
        Width = 1180;
        Height = 780;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildTweaksPage();
        BuildPreferencesPage();
        BuildDebloatPage();
        BuildInstallPage();
        BuildFeaturesPage();
        BuildUpdatesPage();
        BuildNetworkPage();
        BuildLogPage();

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        body.Children.Add(BuildSidebar());
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(22, 16, 22, 10) };
        Grid.SetColumn(main, 1);
        body.Children.Add(main);

        var noticeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        noticeGrid.Children.Add(_noticeText);
        Grid.SetColumn(_noticeAction, 1);
        noticeGrid.Children.Add(_noticeAction);
        var dismiss = new Button { Content = "Dismiss", Classes = { "ghost" }, MinHeight = 28, Padding = new Thickness(10, 2), Margin = new Thickness(8, 0, 0, 0) };
        dismiss.Click += (_, _) => _notice.IsVisible = false;
        Grid.SetColumn(dismiss, 2);
        noticeGrid.Children.Add(dismiss);
        _notice.Child = noticeGrid;
        _noticeAction.Click += async (_, _) =>
        {
            _notice.IsVisible = false;
            if (_noticeRun is { } run) await run();
        };
        main.Children.Add(_notice);

        Grid.SetRow(_pageHost, 1);
        main.Children.Add(_pageHost);

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        bar.Children.Add(_status);
        Grid.SetColumn(_progress, 1);
        bar.Children.Add(_progress);
        Grid.SetColumn(_cancel, 2);
        _cancel.Click += (_, _) => _jobCts?.Cancel();
        bar.Children.Add(_cancel);
        Grid.SetRow(bar, 2);
        main.Children.Add(bar);
        _status.Text = "Ready.";

        Content = PaxStandard.PaxChrome.Wrap(this, body, Title, BrandIcon.Render(36));
        ShowPage("Tweaks");

        if (Program.LimitedReason is { } why && elevated is null)
            ShowNotice(why + " Use Restart as administrator in the sidebar.");

        Closing += (_, e) =>
        {
            if (_busy && !_closeAnyway)
            {
                e.Cancel = true;
                ShowNotice("Something is still running. Wait for it to finish, or press Stop first.", action: ("Close anyway", () =>
                {
                    _closeAnyway = true;
                    _jobCts?.Cancel();
                    Close();
                    return Task.CompletedTask;
                }));
            }
        };

        if (autoLoad)
            Opened += async (_, _) =>
            {
                await RefreshTweaksAsync();
                _ = LoadInstalledAppsAsync();
            };
    }

    private bool _closeAnyway;

    // ---------- Shell ----------

    private Control BuildSidebar()
    {
        var dock = new DockPanel { Background = Sidebar };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(16, 16, 16, 18) };
        head.Children.Add(new Border { Width = 36, Height = 36, Child = new Image { Source = BrandIcon.Render(72), Width = 36, Height = 36 } });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "PAX WINDOWS", FontSize = 13, FontWeight = FontWeight.Bold, LetterSpacing = 1.4, Classes = { "neon" } });
        names.Children.Add(new TextBlock { Text = "TWEAKS", FontSize = 13, FontWeight = FontWeight.Bold, LetterSpacing = 1.4, Classes = { "neon" } });
        head.Children.Add(names);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);

        var foot = new StackPanel { Spacing = 8, Margin = new Thickness(12, 8, 12, 14) };
        var pill = Pill(_elevated ? "Administrator" : "Limited rights", _elevated ? "good" : "warn");
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(pill, _elevated ? "Running as administrator: every tweak can be applied." : "Without administrator rights only your own (HKCU) settings change.");
        foot.Children.Add(pill);
        if (!_elevated)
        {
            var re = new Button { Content = "Restart as administrator", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            re.Click += (_, _) =>
            {
                var why = Elevation.RestartElevated(["--elevated"]);
                if (why is null)
                {
                    _closeAnyway = true;
                    SingleInstance.Release();
                    Close();
                }
                else ShowNotice(why == "cancelled" ? "The administrator prompt was cancelled." : "Couldn't restart as administrator: " + why);
            };
            foot.Children.Add(re);
        }

        var imp = new Button { Content = "Import selection…", HorizontalAlignment = HorizontalAlignment.Stretch };
        imp.Click += async (_, _) => await ImportAsync();
        var exp = new Button { Content = "Export selection…", HorizontalAlignment = HorizontalAlignment.Stretch };
        exp.Click += async (_, _) => await ExportAsync();
        foot.Children.Add(imp);
        foot.Children.Add(exp);
        foot.Children.Add(new TextBlock { Text = $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}", Classes = { "muted" }, FontSize = 11, Margin = new Thickness(4, 2, 0, 0) });
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);

        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0) };
        for (var i = 0; i < PageNames.Length; i++)
        {
            var name = PageNames[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = PageGlyphs[i], Width = 18, TextAlignment = TextAlignment.Center, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Content = row, Classes = { "ghost", "nav" }, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8), MinHeight = 40,
            };
            b.Click += (_, _) => ShowPage(name);
            _nav[name] = b;
            nav.Children.Add(b);
        }

        dock.Children.Add(new ScrollViewer { Content = nav });
        return dock;
    }

    public void ShowPage(string name)
    {
        _page = name;
        _pageHost.Child = _pages[name];
        foreach (var (n, b) in _nav)
        {
            b.Background = n == name ? Brush("#142027") : Brushes.Transparent;
            b.Foreground = n == name ? Good : TextB;
        }

        // Lists that take a few seconds load the first time their page opens.
        if (!SelfTestMode)
        {
            if (name == "Debloat" && !_appxLoaded) _ = LoadAppxAsync();
            if (name == "Features & fixes" && !_featuresLoaded) _ = LoadFeaturesAsync();
            if (name == "Network & power" && !_networkLoaded) _ = LoadNetworkAsync();
            if (name == "Install apps" && _installed is null && !_installLoading) _ = LoadInstalledAppsAsync();
            if (name == "Windows Update" || name == "Preferences") _ = RefreshTweaksAsync();
        }
    }

    /// <summary>Set by the self-test: pages don't start loading things by themselves.</summary>
    public bool SelfTestMode { get; set; }

    public void ShowNotice(string text, bool good = false, (string Label, Func<Task> Run)? action = null)
    {
        _notice.Background = Brush(good ? "#0C2A1D" : "#2A2210");
        _notice.BorderBrush = Brush(good ? "#146B44" : "#7A5A1A");
        _noticeText.Foreground = Brush(good ? "#7DF5BC" : "#F5C97A");
        _noticeText.Text = text;
        _noticeAction.IsVisible = action is not null;
        if (action is { } a)
        {
            _noticeAction.Content = a.Label;
            _noticeRun = a.Run;
        }

        _notice.IsVisible = true;
    }

    // ---------- Jobs ----------

    public void Log(string line)
    {
        void Add()
        {
            _log.AppendLine(line);
            if (_log.Length > 400_000) _log.Remove(0, _log.Length - 300_000);
            _logBox.Text = _log.ToString();
            _logBox.CaretIndex = _logBox.Text.Length;
            var trimmed = line.Trim();
            if (_busy && trimmed.Length > 0) _status.Text = trimmed.Length > 140 ? trimmed[..140] + "…" : trimmed;
        }

        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
    }

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread with the status bar showing it, everything it prints going to the
    /// log, and a notice at the end. The work returns a message for the notice (or null for "Done").
    /// </summary>
    public async Task RunJobAsync(string title, Func<Action<string>, CancellationToken, Task<(bool Ok, string? Message)>> work)
    {
        if (_busy)
        {
            ShowNotice("Something is already running; wait for it to finish.");
            return;
        }

        _busy = true;
        _jobCts = new CancellationTokenSource();
        _progress.IsVisible = true;
        _cancel.IsVisible = true;
        _status.Text = title + "…";
        Log("");
        Log($"── {DateTime.Now:HH:mm:ss} {title} ──");
        var clock = Stopwatch.StartNew();
        try
        {
            var ct = _jobCts.Token;
            var (ok, message) = await Task.Run(() => work(Log, ct), ct);
            Log($"── finished in {clock.Elapsed.TotalSeconds:0.0} s ──");
            ShowNotice(message ?? (ok ? $"{title}: done." : $"{title}: finished with problems. The Log page has the details."), good: ok);
        }
        catch (OperationCanceledException)
        {
            Log("── stopped ──");
            ShowNotice($"{title}: stopped. Anything already changed stays changed.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            ShowNotice($"{title} failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            _progress.IsVisible = false;
            _cancel.IsVisible = false;
            _status.Text = $"Ready. Last: {title} ({clock.Elapsed.TotalSeconds:0} s)";
            _jobCts.Dispose();
            _jobCts = null;
        }
    }

    // ---------- Small building blocks ----------

    public static Border Pill(string text, string kind)
    {
        var b = new Border { Classes = { "pill", kind }, Child = new TextBlock { Text = text } };
        return b;
    }

    private static TextBlock H1(string text) => new() { Text = text, FontSize = 22, FontWeight = FontWeight.Bold };

    private static TextBlock Sub(string text) => new() { Text = text, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };

    private static TextBlock Label(string text) => new() { Text = text, Classes = { "label" }, Margin = new Thickness(2, 14, 0, 6) };

    private static Control Header(string title, string subtitle, params Control[] right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var left = new StackPanel();
        left.Children.Add(H1(title));
        left.Children.Add(Sub(subtitle));
        g.Children.Add(left);
        var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var c in right) r.Children.Add(c);
        Grid.SetColumn(r, 1);
        g.Children.Add(r);
        return g;
    }

    private static Button Btn(string text, bool primary = false, string? tip = null)
    {
        var b = new Button { Content = text };
        if (primary) b.Classes.Add("primary");
        if (tip is not null) ToolTip.SetTip(b, tip);
        return b;
    }

    private static Border Card(Control child, Thickness? padding = null) =>
        new() { Classes = { "card" }, Child = child, Padding = padding ?? new Thickness(16) };

    private static Control PageOf(Control header, Control body)
    {
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(header);
        Grid.SetRow(body, 1);
        g.Children.Add(body);
        return g;
    }

    private static string RestartText(string? r) => r switch
    {
        "explorer" => "Restarts Explorer",
        "signout" => "Sign out",
        "reboot" => "Restart",
        _ => "",
    };

    // ---------- Import / export ----------

    public TweakProfile CurrentSelection()
    {
        var p = new TweakProfile
        {
            Tweaks = _tweakRows.Where(r => r.Value.Box.IsChecked == true).Select(r => r.Key).ToList(),
            Apps = _appRows.Where(r => r.Value.Box.IsChecked == true).Select(r => r.Key).ToList(),
            RemoveApps = _appxRows.Where(r => r.Value.Box.IsChecked == true).Select(r => r.Key).ToList(),
            Features = _featureRows.Where(r => r.Value.Box.IsChecked == true).Select(r => r.Key).ToList(),
        };
        foreach (var (id, sw) in _toggles)
        {
            var t = Catalog.Find(id)!;
            if (sw.IsChecked is { } on && on != t.DefaultOn) p.Preferences[id] = on;
        }

        foreach (var t in Catalog.Tweaks.Tweaks.Where(t => t.IsChoice))
            if (Engine.CurrentChoice(t) is { } c && !c.Name.Equals(t.DefaultChoice, StringComparison.OrdinalIgnoreCase))
                p.Choices[t.Id] = c.Name;
        return p;
    }

    private async Task ExportAsync()
    {
        var p = CurrentSelection();
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export selection",
                SuggestedFileName = $"PaxWindowsTweaks-{Environment.MachineName}.json",
                FileTypeChoices = [new FilePickerFileType("PAX Windows Tweaks selection") { Patterns = ["*.json"] }],
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            p.Save(path);
            ShowNotice($"Saved {p.Tweaks.Count} tweak(s), {p.Preferences.Count + p.Choices.Count} preference(s), {p.Apps.Count} app(s), {p.RemoveApps.Count} app removal(s) and {p.Features.Count} feature(s) to {path}. " +
                       "Import it on another PC, or run it there with PaxWindowsTweaks.exe --apply.", good: true);
        }
        catch (Exception ex)
        {
            ShowNotice("Couldn't save the selection: " + ex.Message);
        }
    }

    private async Task ImportAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import selection",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PAX Windows Tweaks selection") { Patterns = ["*.json"] }],
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var p = TweakProfile.Load(path);
            LoadSelection(p);
            ShowNotice($"Loaded {Path.GetFileName(path)}: {p.Tweaks.Count} tweak(s), {p.Preferences.Count + p.Choices.Count} preference(s), {p.Apps.Count} app(s), {p.RemoveApps.Count} removal(s), {p.Features.Count} feature(s). They're ticked on their pages.",
                good: true, action: ("Run it all now", () => RunJobAsync("Applying the imported selection", async (log, ct) =>
                {
                    var failed = await p.RunAsync(log, ct);
                    await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
                    return (failed == 0, failed == 0 ? "Everything in the selection was applied." : $"{failed} item(s) had problems; see the Log page.");
                })));
        }
        catch (Exception ex)
        {
            ShowNotice("Couldn't read that file: " + ex.Message);
        }
    }

    public void LoadSelection(TweakProfile p)
    {
        foreach (var (id, r) in _tweakRows) r.Box.IsChecked = p.Tweaks.Contains(id, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, r) in _appRows) r.Box.IsChecked = p.Apps.Contains(id, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, r) in _appxRows) r.Box.IsChecked = p.RemoveApps.Contains(id, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, r) in _featureRows) r.Box.IsChecked = p.Features.Contains(id, StringComparer.OrdinalIgnoreCase);
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        UpdateTweakCount();
        UpdateAppCount();
    }
}
