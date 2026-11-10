using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using pxdWindowsTweaks.Core;

namespace pxdWindowsTweaks.Views;

public sealed partial class MainWindow
{
    private sealed record AppRow(AppItem App, CheckBox Box, Border Installed, Control Row);

    private sealed record AppxRow(BloatApp App, CheckBox Box, Border Installed, Control Row);

    private readonly Dictionary<string, AppRow> _appRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AppxRow> _appxRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _appSearch = new() { PlaceholderText = "Search 239 apps", Width = 240 };
    private readonly ComboBox _appCategory = new() { MinWidth = 170 };
    private readonly CheckBox _appInstalledOnly = new() { Content = "Installed only", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _appCount = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _appxState = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private HashSet<string>? _installed;
    private bool _installLoading, _appxLoaded;
    private Button? _removeAppx;
    private DateTime _removeArmed;

    // ---------- Debloat ----------

    private void BuildDebloatPage()
    {
        _removeAppx = Btn("Remove selected", primary: true, "Removes the ticked apps for every user and stops Windows adding them for new users.");
        _removeAppx.Click += async (_, _) => await RemoveAppxAsync();
        var refresh = Btn("Refresh");
        refresh.Click += async (_, _) => await LoadAppxAsync();

        var select = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        select.Children.Add(new TextBlock { Text = "Select:", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        var rec = Btn("Recommended");
        rec.Click += (_, _) =>
        {
            foreach (var r in _appxRows.Values) r.Box.IsChecked = r.App.Recommended && r.Installed.IsVisible;
        };
        var none = Btn("None");
        none.Click += (_, _) =>
        {
            foreach (var r in _appxRows.Values) r.Box.IsChecked = false;
        };
        foreach (var b in new[] { rec, none })
        {
            b.MinHeight = 32;
            b.Padding = new Thickness(12, 4);
            select.Children.Add(b);
        }

        select.Children.Add(_appxState);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 0, 14, 8) };
        var left = new StackPanel();
        var right = new StackPanel();
        grid.Children.Add(Card(left, new Thickness(0, 4)));
        var rc = Card(right, new Thickness(0, 4));
        Grid.SetColumn(rc, 2);
        grid.Children.Add(rc);
        for (var i = 0; i < SystemTools.Bloat.Length; i++)
        {
            var a = SystemTools.Bloat[i];
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            name.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (a.Recommended) name.Children.Add(Pill("Bloat", "warn"));
            text.Children.Add(name);
            text.Children.Add(new TextBlock { Text = a.Note.Length > 0 ? a.Note : a.Package, Classes = { a.Note.Length > 0 ? "muted" : "mono" }, Foreground = Muted, FontSize = 11 });
            var inst = Pill("Installed", "good");
            inst.IsVisible = false;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 7) };
            g.Children.Add(box);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            Grid.SetColumn(inst, 2);
            g.Children.Add(inst);
            var row = new Border { Child = g, Background = Brushes.Transparent };
            row.PointerReleased += (_, e) =>
            {
                if (e.Source is TextBlock or Border or Grid or StackPanel) box.IsChecked = box.IsChecked != true;
            };
            (i < (SystemTools.Bloat.Length + 1) / 2 ? left : right).Children.Add(row);
            _appxRows[a.Package] = new AppxRow(a, box, inst, row);
        }

        var top = new StackPanel();
        top.Children.Add(Header("Debloat", "Built-in Store apps you can remove. Most come back from the Microsoft Store if you change your mind.", refresh, _removeAppx));
        top.Children.Add(select);
        _pages["Debloat"] = PageOf(top, new ScrollViewer { Content = grid });
        _appxState.Text = "Open this page to check which are installed.";
    }

    public async Task LoadAppxAsync()
    {
        _appxState.Text = "Checking installed apps…";
        try
        {
            var have = await SystemTools.InstalledAppxAsync();
            _appxLoaded = true;
            foreach (var r in _appxRows.Values)
            {
                var on = have.Contains(r.App.Package);
                r.Installed.IsVisible = on;
                r.Row.Opacity = on ? 1 : 0.45;
                if (!on) r.Box.IsChecked = false;
            }

            _appxState.Text = $"{have.Count} of {_appxRows.Count} installed. Greyed-out ones aren't on this PC.";
        }
        catch (Exception ex)
        {
            _appxState.Text = "Couldn't list the installed apps: " + ex.Message.Split('\n')[0];
        }
    }

    private async Task RemoveAppxAsync()
    {
        var chosen = _appxRows.Values.Where(r => r.Box.IsChecked == true).Select(r => r.App).ToList();
        if (chosen.Count == 0)
        {
            ShowNotice("Tick the apps to remove first, or pick Recommended.");
            return;
        }

        // Two clicks: the first arms the button, so a stray click can't uninstall anything.
        if (DateTime.UtcNow - _removeArmed > TimeSpan.FromSeconds(6))
        {
            _removeArmed = DateTime.UtcNow;
            _removeAppx!.Content = $"Click again to remove {chosen.Count}";
            DispatcherTimer.RunOnce(() => _removeAppx.Content = "Remove selected", TimeSpan.FromSeconds(6));
            return;
        }

        _removeArmed = default;
        _removeAppx!.Content = "Remove selected";
        await RunJobAsync($"Removing {chosen.Count} app(s)", async (log, ct) =>
        {
            var code = await SystemTools.RemoveAppxAsync(chosen.Select(c => c.Package), log, ct);
            await Dispatcher.UIThread.InvokeAsync(LoadAppxAsync);
            return (code == 0, code == 0 ? $"Removed {chosen.Count} app(s)." : null);
        });
    }

    // ---------- Install ----------

    private void BuildInstallPage()
    {
        var install = Btn("Install selected", primary: true);
        var upgrade = Btn("Upgrade selected");
        var uninstall = Btn("Uninstall selected");
        var upgradeAll = Btn("Upgrade everything", tip: "winget upgrade --all: updates every program winget can update, not only these.");
        install.Click += async (_, _) => await AppJobAsync("Installing", Winget.InstallAsync);
        upgrade.Click += async (_, _) => await AppJobAsync("Upgrading", Winget.UpgradeAsync);
        uninstall.Click += async (_, _) => await AppJobAsync("Uninstalling", Winget.UninstallAsync);
        upgradeAll.Click += async (_, _) => await RunJobAsync("Upgrading everything", async (log, ct) =>
        {
            var code = await Winget.UpgradeAllAsync(log, ct);
            await Dispatcher.UIThread.InvokeAsync(LoadInstalledAppsAsync);
            return (Winget.Succeeded(code), Winget.Succeeded(code) ? "Every program winget can update is up to date." : null);
        });

        _appCategory.Items.Add("All categories");
        foreach (var c in Catalog.Apps.Select(a => a.Category).Distinct().OrderBy(c => c)) _appCategory.Items.Add(c);
        _appCategory.SelectedIndex = 0;
        _appCategory.SelectionChanged += (_, _) => FilterApps();
        _appSearch.TextChanged += (_, _) => FilterApps();
        _appInstalledOnly.IsCheckedChanged += (_, _) => FilterApps();
        var selInstalled = Btn("Tick installed", tip: "Ticks every listed app that's installed, ready for Upgrade selected.");
        selInstalled.Click += (_, _) =>
        {
            foreach (var r in _appRows.Values) r.Box.IsChecked = _installed?.Contains(r.App.Id) == true && r.Row.IsVisible;
        };
        var clear = Btn("None");
        clear.Click += (_, _) =>
        {
            foreach (var r in _appRows.Values) r.Box.IsChecked = false;
        };
        var refresh = Btn("Refresh");
        refresh.Click += async (_, _) => await LoadInstalledAppsAsync();

        var filters = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        filters.Children.Add(_appSearch);
        filters.Children.Add(_appCategory);
        filters.Children.Add(_appInstalledOnly);
        foreach (var b in new[] { selInstalled, clear, refresh })
        {
            b.MinHeight = 32;
            b.Padding = new Thickness(12, 4);
            filters.Children.Add(b);
        }

        filters.Children.Add(_appCount);

        var list = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
        foreach (var group in Catalog.Apps.GroupBy(a => a.Category).OrderBy(g => g.Key))
        {
            var header = Label(group.Key.ToUpperInvariant());
            list.Children.Add(header);
            var wrap = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*") };
            var cols = new[] { new StackPanel { Spacing = 6 }, new StackPanel { Spacing = 6 }, new StackPanel { Spacing = 6 } };
            for (var c = 0; c < 3; c++)
            {
                Grid.SetColumn(cols[c], c * 2);
                wrap.Children.Add(cols[c]);
            }

            var i = 0;
            foreach (var a in group.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
                box.IsCheckedChanged += (_, _) => UpdateAppCount();
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = a.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = a.Id, Classes = { "mono" }, Foreground = Muted, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis });
                var inst = Pill("Installed", "good");
                inst.IsVisible = false;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                g.Children.Add(box);
                Grid.SetColumn(text, 1);
                g.Children.Add(text);
                Grid.SetColumn(inst, 2);
                g.Children.Add(inst);
                var card = new Border { Classes = { "card" }, Padding = new Thickness(10, 6), Child = g };
                ToolTip.SetTip(card, a.Description + (a.Foss ? "\nFree and open source." : "") + (a.Link.Length > 0 ? "\n" + a.Link : ""));
                card.PointerReleased += (_, e) =>
                {
                    if (e.Source is TextBlock or Border or Grid or StackPanel) box.IsChecked = box.IsChecked != true;
                };
                cols[i % 3].Children.Add(card);
                _appRows[a.Id] = new AppRow(a, box, inst, card);
                i++;
            }

            wrap.Tag = header;
            list.Children.Add(wrap);
        }

        var top = new StackPanel();
        top.Children.Add(Header("Install apps", "Tick programs and install them in one go with winget, silently, from each publisher's own installer.", uninstall, upgrade, upgradeAll, install));
        top.Children.Add(filters);
        _pages["Install apps"] = PageOf(top, new ScrollViewer { Content = list });
        _appList = list;
        UpdateAppCount();
    }

    private StackPanel? _appList;

    private void FilterApps()
    {
        var q = _appSearch.Text?.Trim() ?? "";
        var cat = _appCategory.SelectedIndex <= 0 ? null : _appCategory.SelectedItem as string;
        var onlyInstalled = _appInstalledOnly.IsChecked == true;
        foreach (var r in _appRows.Values)
        {
            r.Row.IsVisible = (cat is null || r.App.Category == cat)
                              && (!onlyInstalled || _installed?.Contains(r.App.Id) == true)
                              && (q.Length == 0 || r.App.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.App.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || r.App.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Hide category headings with nothing left under them.
        if (_appList is null) return;
        foreach (var grid in _appList.Children.OfType<Grid>())
        {
            var any = grid.Children.OfType<StackPanel>().SelectMany(s => s.Children).Any(c => c.IsVisible);
            grid.IsVisible = any;
            if (grid.Tag is Control h) h.IsVisible = any;
        }
    }

    private void UpdateAppCount()
    {
        var n = _appRows.Values.Count(r => r.Box.IsChecked == true);
        var inst = _installed is null ? (_installLoading ? "checking what's installed…" : (Winget.Available ? "" : "winget isn't installed (Features & fixes → Reinstall winget)")) : $"{_appRows.Keys.Count(k => _installed.Contains(k))} installed";
        _appCount.Text = string.Join(" · ", new[] { n > 0 ? $"{n} selected" : "", inst }.Where(s => s.Length > 0));
    }

    public async Task LoadInstalledAppsAsync()
    {
        if (!Winget.Available)
        {
            UpdateAppCount();
            return;
        }

        _installLoading = true;
        UpdateAppCount();
        try
        {
            _installed = await Winget.InstalledAsync();
        }
        catch (Exception ex)
        {
            Log("Couldn't read installed apps from winget: " + ex.Message);
        }
        finally
        {
            _installLoading = false;
        }

        foreach (var r in _appRows.Values) r.Installed.IsVisible = _installed?.Contains(r.App.Id) == true;
        UpdateAppCount();
        FilterApps();
    }

    private async Task AppJobAsync(string verb, Func<string, Action<string>, CancellationToken, Task<int>> run)
    {
        var chosen = _appRows.Values.Where(r => r.Box.IsChecked == true).Select(r => r.App).ToList();
        if (chosen.Count == 0)
        {
            ShowNotice("Tick some apps first.");
            return;
        }

        await RunJobAsync($"{verb} {chosen.Count} app(s)", async (log, ct) =>
        {
            var failed = new List<string>();
            var n = 0;
            foreach (var a in chosen)
            {
                ct.ThrowIfCancellationRequested();
                n++;
                log($"▶ {verb} {a.Name} ({n}/{chosen.Count})");
                var code = await run(a.Id, l => log("  " + l), ct);
                log("  " + Winget.Describe(code));
                if (!Winget.Succeeded(code)) failed.Add(a.Name);
            }

            await Dispatcher.UIThread.InvokeAsync(LoadInstalledAppsAsync);
            return (failed.Count == 0, failed.Count == 0
                ? $"{verb} {chosen.Count} app(s): done."
                : $"{failed.Count} of {chosen.Count} didn't work: {string.Join(", ", failed)}. The Log page has winget's output.");
        });
    }
}
