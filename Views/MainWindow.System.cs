using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaxWindowsTweaks.Core;

namespace PaxWindowsTweaks.Views;

public sealed partial class MainWindow
{
    private sealed record FeatureRow(FeatureItem Feature, CheckBox Box, Border State);

    private readonly Dictionary<string, FeatureRow> _featureRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _featureNote = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _dnsBox = new() { MinWidth = 260 };
    private readonly TextBlock _dnsDescription = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _dnsCurrent = new() { Spacing = 4 };
    private readonly TextBlock _powerPlan = new() { FontSize = 16, FontWeight = FontWeight.SemiBold };
    private bool _featuresLoaded, _networkLoaded;

    // ---------- Features & fixes ----------

    private void BuildFeaturesPage()
    {
        var feats = new StackPanel { Spacing = 0 };
        foreach (var f in SystemTools.Features)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = f.Name, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = f.Description, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var state = Pill("", "");
            state.IsVisible = false;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6) };
            g.Children.Add(box);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            Grid.SetColumn(state, 2);
            state.Margin = new Thickness(10, 0, 0, 0);
            g.Children.Add(state);
            feats.Children.Add(g);
            _featureRows[f.Id] = new FeatureRow(f, box, state);
        }

        var on = Btn("Turn on selected", primary: true);
        var off = Btn("Turn off selected");
        on.Click += async (_, _) => await FeatureJobAsync(true);
        off.Click += async (_, _) => await FeatureJobAsync(false);
        var featBtns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        featBtns.Children.Add(on);
        featBtns.Children.Add(off);
        var featCard = new StackPanel();
        featCard.Children.Add(new TextBlock { Text = "WINDOWS FEATURES", Classes = { "label" } });
        featCard.Children.Add(_featureNote);
        featCard.Children.Add(feats);
        featCard.Children.Add(featBtns);

        var fixes = new StackPanel { Spacing = 0 };
        fixes.Children.Add(new TextBlock { Text = "FIXES AND ONE-OFF JOBS", Classes = { "label" }, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var fx in SystemTools.Fixes)
        {
            var text = new StackPanel();
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            name.Children.Add(new TextBlock { Text = fx.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (fx.Caution is { } c)
            {
                var p = Pill("Caution", "bad");
                ToolTip.SetTip(p, c);
                name.Children.Add(p);
            }

            if (fx.Long) name.Children.Add(Pill("Takes a while", "info"));
            text.Children.Add(name);
            text.Children.Add(new TextBlock { Text = fx.Description, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var run = Btn("Run");
            run.MinHeight = 32;
            run.Padding = new Thickness(14, 4);
            run.VerticalAlignment = VerticalAlignment.Center;
            run.Click += async (_, _) => await RunJobAsync(fx.Name, async (log, ct) =>
            {
                var script = Catalog.Script(fx.Script) ?? throw new InvalidOperationException("script missing: " + fx.Script);
                var code = await Shell.RunScriptAsync(script, log, ct);
                return (code == 0, code == 0 ? $"{fx.Name}: done." : null);
            });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6) };
            g.Children.Add(text);
            Grid.SetColumn(run, 1);
            run.Margin = new Thickness(12, 0, 0, 0);
            g.Children.Add(run);
            fixes.Children.Add(g);
        }

        var panels = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var p in SystemTools.Panels)
        {
            var b = Btn(p.Name);
            b.MinHeight = 32;
            b.Padding = new Thickness(12, 4);
            b.Click += (_, _) =>
            {
                try { Shell.Open(p.File, p.Args); }
                catch (Exception ex) { ShowNotice($"Couldn't open {p.Name}: {ex.Message}"); }
            };
            panels.Children.Add(b);
        }

        var panelCard = new StackPanel { Spacing = 8 };
        panelCard.Children.Add(new TextBlock { Text = "CLASSIC CONTROL PANELS", Classes = { "label" } });
        panelCard.Children.Add(panels);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 0, 14, 8) };
        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Card(featCard));
        left.Children.Add(Card(panelCard));
        grid.Children.Add(left);
        var right = Card(fixes);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        _featureNote.Text = "Open this page to read which are on.";
        _pages["Features & fixes"] = PageOf(Header("Features & fixes", "Windows optional features, repairs, and the old control panels Windows 11 hides."), new ScrollViewer { Content = grid });
    }

    public async Task LoadFeaturesAsync()
    {
        _featureNote.Text = "Reading which features are on…";
        try
        {
            var states = await SystemTools.FeatureStatesAsync();
            _featuresLoaded = true;
            foreach (var r in _featureRows.Values)
            {
                var known = r.Feature.Features.Where(states.ContainsKey).ToList();
                if (known.Count == 0)
                {
                    SetPill(r.State, "Not on this edition", "");
                    r.Box.IsEnabled = false;
                    continue;
                }

                var enabled = known.Count(f => states[f].StartsWith("Enabled", StringComparison.OrdinalIgnoreCase));
                var pending = known.Any(f => states[f].Contains("Pending", StringComparison.OrdinalIgnoreCase));
                if (pending) SetPill(r.State, "Restart pending", "warn");
                else if (enabled == known.Count) SetPill(r.State, "On", "good");
                else if (enabled > 0) SetPill(r.State, "Partly on", "warn");
                else SetPill(r.State, "Off", "");
            }

            _featureNote.Text = "Tick features, then turn them on or off. Most need a restart afterwards.";
        }
        catch (Exception ex)
        {
            _featureNote.Text = _elevated
                ? "Couldn't read the optional features: " + ex.Message.Split('\n')[0]
                : "Reading Windows features needs administrator rights.";
        }
    }

    private static void SetPill(Border pill, string text, string kind)
    {
        if (pill.Child is TextBlock tb) tb.Text = text;
        pill.Classes.Set("good", kind == "good");
        pill.Classes.Set("warn", kind == "warn");
        pill.IsVisible = text.Length > 0;
    }

    private async Task FeatureJobAsync(bool enable)
    {
        var chosen = _featureRows.Values.Where(r => r.Box.IsChecked == true).Select(r => r.Feature).ToList();
        if (chosen.Count == 0)
        {
            ShowNotice("Tick the features first.");
            return;
        }

        await RunJobAsync($"{(enable ? "Turning on" : "Turning off")} {chosen.Count} feature(s)", async (log, ct) =>
        {
            var code = await SystemTools.SetFeaturesAsync(chosen.SelectMany(f => f.Features), enable, log, ct);
            await Dispatcher.UIThread.InvokeAsync(LoadFeaturesAsync);
            return (code == 0, code == 0 ? "Done. Restart the PC if Windows asks for it." : null);
        });
    }

    // ---------- Network & power ----------

    private void BuildNetworkPage()
    {
        foreach (var p in SystemTools.Dns) _dnsBox.Items.Add(p.Name);
        _dnsBox.SelectionChanged += (_, _) =>
        {
            if (_dnsBox.SelectedIndex >= 0)
            {
                var p = SystemTools.Dns[_dnsBox.SelectedIndex];
                _dnsDescription.Text = p.V4a is null ? p.Description : $"{p.Description}  {p.V4a}, {p.V4b} · {p.V6a}, {p.V6b}";
            }
        };
        _dnsBox.SelectedIndex = 1;
        var apply = Btn("Use on all adapters", primary: true);
        apply.Click += async (_, _) =>
        {
            if (_dnsBox.SelectedIndex < 0) return;
            var p = SystemTools.Dns[_dnsBox.SelectedIndex];
            await RunJobAsync($"DNS: {p.Name}", async (log, ct) =>
            {
                var code = await SystemTools.SetDnsAsync(p, log, ct);
                await Dispatcher.UIThread.InvokeAsync(LoadNetworkAsync);
                return (code == 0, code == 0 ? $"Every connected network adapter now uses {p.Name}." : null);
            });
        };
        var dnsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        dnsRow.Children.Add(_dnsBox);
        dnsRow.Children.Add(apply);
        var dns = new StackPanel { Spacing = 10 };
        dns.Children.Add(new TextBlock { Text = "DNS SERVERS", Classes = { "label" } });
        dns.Children.Add(dnsRow);
        dns.Children.Add(_dnsDescription);
        dns.Children.Add(new TextBlock { Text = "Now in use", Classes = { "muted" }, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        dns.Children.Add(_dnsCurrent);

        var ult = Btn("Add Ultimate Performance", primary: true, "Adds Windows' hidden Ultimate Performance plan and switches to it. Not for laptops on battery.");
        ult.Click += async (_, _) => await ScriptJobAsync("Ultimate Performance plan", "power-ultimate-on.ps1", LoadNetworkAsync);
        var restore = Btn("Restore default plans", tip: "powercfg /restoredefaultschemes: back to Balanced, removes added plans.");
        restore.Click += async (_, _) => await ScriptJobAsync("Default power plans", "power-ultimate-off.ps1", LoadNetworkAsync);
        var opts = Btn("Power Options");
        opts.Click += (_, _) => Shell.Open("powercfg.cpl");
        var powerBtns = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        powerBtns.Children.Add(ult);
        powerBtns.Children.Add(restore);
        powerBtns.Children.Add(opts);
        var power = new StackPanel { Spacing = 10 };
        power.Children.Add(new TextBlock { Text = "POWER PLAN", Classes = { "label" } });
        power.Children.Add(_powerPlan);
        power.Children.Add(new TextBlock { Text = "Ultimate Performance stops the processor and devices from saving power between bursts of work. Good for desktops; it uses more electricity. Hibernation is on the Tweaks page.", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        power.Children.Add(powerBtns);

        var flush = Btn("Flush DNS cache");
        flush.Click += async (_, _) => await RunJobAsync("Flushing the DNS cache", async (log, ct) =>
        {
            var code = await Shell.RunAsync("ipconfig.exe", "/flushdns", log, ct, System.Text.Encoding.Default);
            return (code == 0, code == 0 ? "DNS cache flushed." : null);
        });
        var net = Btn("Reset networking…");
        net.Click += (_, _) => ShowPage("Features & fixes");
        var tools = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        tools.Children.Add(flush);
        tools.Children.Add(net);
        dns.Children.Add(tools);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 0, 14, 8) };
        grid.Children.Add(Card(dns));
        var pc = Card(power);
        pc.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(pc, 2);
        grid.Children.Add(pc);
        _powerPlan.Text = "…";
        _pages["Network & power"] = PageOf(Header("Network & power", "DNS servers for every connected adapter, and power plans. IPv4/IPv6 and Teredo tweaks are on the Tweaks page."), new ScrollViewer { Content = grid });
    }

    public async Task LoadNetworkAsync()
    {
        try
        {
            var current = await SystemTools.CurrentDnsAsync();
            _networkLoaded = true;
            _dnsCurrent.Children.Clear();
            foreach (var a in current)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
                g.Children.Add(new TextBlock { Text = a.Adapter, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var provider = SystemTools.Dns.FirstOrDefault(p => p.V4a is not null && a.Servers.Contains(p.V4a));
                var s = new TextBlock { Text = (provider is null ? "" : provider.Name + ": ") + (a.Servers.Length == 0 ? "none" : a.Servers), Classes = { "mono" }, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(s, 1);
                g.Children.Add(s);
                _dnsCurrent.Children.Add(g);
            }

            if (current.Count == 0) _dnsCurrent.Children.Add(new TextBlock { Text = "No connected adapters.", Classes = { "muted" } });
        }
        catch (Exception ex)
        {
            _dnsCurrent.Children.Clear();
            _dnsCurrent.Children.Add(new TextBlock { Text = "Couldn't read DNS settings: " + ex.Message.Split('\n')[0], Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        }

        try
        {
            _powerPlan.Text = "Active: " + await SystemTools.ActivePowerPlanAsync();
        }
        catch (Exception ex)
        {
            _powerPlan.Text = "Couldn't read the power plan: " + ex.Message;
        }
    }

    private Task ScriptJobAsync(string title, string script, Func<Task> after) => RunJobAsync(title, async (log, ct) =>
    {
        var code = await Shell.RunScriptAsync(Catalog.Script(script)!, log, ct);
        await Dispatcher.UIThread.InvokeAsync(after);
        return (code == 0, null);
    });

    // ---------- Log ----------

    private void BuildLogPage()
    {
        var copy = Btn("Copy");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard is { } cb) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(cb, _log.ToString());
                ShowNotice("Log copied to the clipboard.", good: true);
            }
            catch (Exception ex)
            {
                ShowNotice("Couldn't copy: " + ex.Message);
            }
        };
        var save = Btn("Save…");
        save.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save log",
                SuggestedFileName = $"PaxWindowsTweaks-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                FileTypeChoices = [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }],
            });
            if (file?.TryGetLocalPath() is { } path)
            {
                await File.WriteAllTextAsync(path, _log.ToString());
                ShowNotice("Log saved to " + path, good: true);
            }
        };
        var clear = Btn("Clear");
        clear.Click += (_, _) =>
        {
            _log.Clear();
            _logBox.Text = "";
        };
        var undoFolder = Btn("Open undo records", tip: "Where the values from before each tweak are kept.");
        undoFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(Engine.UndoFolder);
            Shell.Open(Engine.UndoFolder);
        };
        _pages["Log"] = PageOf(Header("Log", "Everything the app changed and what each tool printed, this session.", undoFolder, clear, save, copy), _logBox);
        Log($"PAX Windows Tweaks {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}, {(_elevated ? "administrator" : "standard user")}, {Environment.OSVersion.VersionString}");
    }
}
