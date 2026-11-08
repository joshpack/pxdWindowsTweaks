using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PaxWindowsTweaks.Core;

namespace PaxWindowsTweaks.Views;

public sealed partial class MainWindow
{
    private sealed record TweakRow(Tweak Tweak, CheckBox Box, Border Status, Control Row);

    private readonly Dictionary<string, TweakRow> _tweakRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToggleSwitch> _toggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _toggleStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _choiceBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (Button Use, Border Current, Border Card)> _updateCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _tweakCount = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _tweakSearch = new() { PlaceholderText = "Search tweaks", Width = 220 };
    private readonly TextBlock _updateNow = new() { Classes = { "muted" }, Margin = new Thickness(0, 0, 0, 10) };

    /// <summary>Tests and screenshots: states to show instead of reading this PC.</summary>
    public Dictionary<string, TweakState>? FakeStates { get; set; }

    // ---------- Tweaks ----------

    private void BuildTweaksPage()
    {
        var apply = Btn("Apply selected", primary: true, "Applies every ticked tweak. A restore point is made first when 'Create a restore point' is ticked.");
        var undo = Btn("Undo selected", tip: "Puts back what was there before (this PC's own values, recorded when the tweak was applied here), or Windows' defaults.");
        apply.Click += async (_, _) => await ApplyTweaksAsync(undo: false);
        undo.Click += async (_, _) => await ApplyTweaksAsync(undo: true);

        var presets = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        presets.Children.Add(new TextBlock { Text = "Select:", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center });
        foreach (var name in Catalog.Tweaks.Presets.Keys)
        {
            var b = Btn(name, tip: name switch
            {
                "Standard" => "The safe set most PCs should have",
                "Minimal" => "Only the privacy basics",
                "Advanced" => "Standard plus Widgets, OneDrive, Windows AI, Store search, the old Start and right-click menus",
                _ => null,
            });
            b.MinHeight = 32;
            b.Padding = new Thickness(12, 4);
            b.Click += (_, _) => SelectPreset(name);
            presets.Children.Add(b);
        }

        var clear = Btn("None");
        clear.MinHeight = 32;
        clear.Padding = new Thickness(12, 4);
        clear.Click += (_, _) => SelectPreset(null);
        presets.Children.Add(clear);
        presets.Children.Add(_tweakSearch);
        presets.Children.Add(_tweakCount);
        _tweakSearch.TextChanged += (_, _) => FilterTweaks();

        var list = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 14, 8) };
        list.Children.Add(Label("ESSENTIAL"));
        list.Children.Add(TweakGroup("essential"));
        list.Children.Add(Label("ADVANCED: READ EACH ONE FIRST"));
        list.Children.Add(TweakGroup("advanced"));

        var top = new StackPanel();
        top.Children.Add(Header("Tweaks", "Tick what you want, then Apply. Every change can be undone; what was there before is recorded first.", undo, apply));
        top.Children.Add(presets);
        _pages["Tweaks"] = PageOf(top, new ScrollViewer { Content = list, Margin = new Thickness(0, 8, 0, 0) });
        UpdateTweakCount();
    }

    private Control TweakGroup(string group)
    {
        var card = new StackPanel();
        var first = true;
        foreach (var t in Catalog.Tweaks.Tweaks.Where(t => t.Group == group))
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 6, 0) };
            box.IsCheckedChanged += (_, _) => UpdateTweakCount();
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5 });
            text.Children.Add(new TextBlock { Text = t.Description, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
            if (t.Caution is { } c)
            {
                var p = Pill("Caution", "bad");
                ToolTip.SetTip(p, c);
                pills.Children.Add(p);
            }

            if (RestartText(t.Restart) is { Length: > 0 } r && t.Restart != "explorer") pills.Children.Add(Pill(r, "info"));
            if (t.IsAction) pills.Children.Add(Pill(t.CanUndo && t.OffScript is not null ? "One-off, undo available" : "One-off", "info"));
            var status = Pill("…", "");
            status.IsVisible = false;
            pills.Children.Add(status);

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 10) };
            g.Children.Add(box);
            Grid.SetColumn(text, 1);
            g.Children.Add(text);
            Grid.SetColumn(pills, 2);
            pills.Margin = new Thickness(12, 0, 0, 0);
            g.Children.Add(pills);
            // Clicking anywhere on the row ticks it.
            var row = new Border { Child = g, Background = Brushes.Transparent, BorderBrush = Brush("#1E2330"), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
            row.PointerReleased += (_, e) =>
            {
                if (e.Source is TextBlock or Border or Grid or StackPanel) box.IsChecked = box.IsChecked != true;
            };
            first = false;
            card.Children.Add(row);
            _tweakRows[t.Id] = new TweakRow(t, box, status, row);
        }

        return Card(card, new Thickness(0, 4));
    }

    private void SelectPreset(string? name)
    {
        var ids = name is null ? [] : Catalog.Tweaks.Presets[name];
        foreach (var (id, r) in _tweakRows) r.Box.IsChecked = ids.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    private void FilterTweaks()
    {
        var q = _tweakSearch.Text?.Trim() ?? "";
        foreach (var r in _tweakRows.Values)
            r.Row.IsVisible = q.Length == 0 || r.Tweak.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Tweak.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateTweakCount()
    {
        var n = _tweakRows.Values.Count(r => r.Box.IsChecked == true);
        _tweakCount.Text = n == 0 ? "" : $"{n} selected";
    }

    private async Task ApplyTweaksAsync(bool undo)
    {
        var chosen = _tweakRows.Values.Where(r => r.Box.IsChecked == true).Select(r => r.Tweak).ToList();
        if (undo) chosen = chosen.Where(t => t.CanUndo).ToList();
        if (chosen.Count == 0)
        {
            ShowNotice(undo ? "Tick the tweaks to undo first (one-off actions without an undo are skipped)." : "Tick some tweaks first, or pick Standard.");
            return;
        }

        // A restore point goes first, everything else in list order.
        chosen = chosen.OrderBy(t => t.Id == "restore-point" ? 0 : 1).ToList();
        await RunJobAsync(undo ? $"Undoing {chosen.Count} tweak(s)" : $"Applying {chosen.Count} tweak(s)", async (log, ct) =>
        {
            var failed = 0;
            foreach (var t in chosen)
            {
                ct.ThrowIfCancellationRequested();
                if (undo && t.Id == "restore-point") continue;
                var ok = undo ? await Engine.UndoAsync(t, log, ct) : await Engine.ApplyAsync(t, log, ct);
                if (!ok) failed++;
            }

            var hint = Engine.FinishBatch(chosen, log);
            await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
            var msg = failed == 0
                ? $"{(undo ? "Undid" : "Applied")} {chosen.Count} tweak(s). {hint}"
                : $"{failed} of {chosen.Count} tweak(s) had problems (see the Log page). {hint}";
            return (failed == 0, msg.Trim());
        });
    }

    /// <summary>Reads every tweak's state (off the UI thread) and updates pills, switches and the update cards.</summary>
    public async Task RefreshTweaksAsync()
    {
        var states = FakeStates ?? await Task.Run(() => Catalog.Tweaks.Tweaks.Where(t => !t.IsChoice).ToDictionary(t => t.Id, Engine.State, StringComparer.OrdinalIgnoreCase));
        var choices = await Task.Run(() => Catalog.Tweaks.Tweaks.Where(t => t.IsChoice).ToDictionary(t => t.Id, t => FakeStates is null ? Engine.CurrentChoice(t)?.Name : t.DefaultChoice));
        _refreshing = true;
        try
        {
            foreach (var (id, r) in _tweakRows)
            {
                var s = states.GetValueOrDefault(id, TweakState.Unknown);
                SetStatusPill(r.Status, s, r.Tweak);
            }

            foreach (var (id, sw) in _toggles)
            {
                var s = states.GetValueOrDefault(id, TweakState.Unknown);
                sw.IsChecked = s switch
                {
                    TweakState.Applied => true,
                    TweakState.NotApplied => false,
                    _ => Catalog.Find(id)!.DefaultOn,
                };
                var pill = _toggleStatus[id];
                pill.IsVisible = s == TweakState.Partial;
            }

            foreach (var (id, box) in _choiceBoxes)
            {
                var t = Catalog.Find(id)!;
                var current = choices.GetValueOrDefault(id);
                box.SelectedIndex = current is null ? -1 : t.Choices.FindIndex(c => c.Name == current);
                box.PlaceholderText = current is null ? "Custom" : null;
            }

            var mode = choices.GetValueOrDefault("updates");
            foreach (var (name, card) in _updateCards)
            {
                var isCurrent = name == mode;
                card.Current.IsVisible = isCurrent;
                card.Use.IsEnabled = !isCurrent;
                card.Card.BorderBrush = isCurrent ? Brush("#146B44") : Brush("#272C39");
            }

            _updateNow.Text = mode is null
                ? "Current: a custom mix (set by hand, Group Policy or another tool). Pick one below to tidy it up."
                : $"Current: {mode}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void SetStatusPill(Border pill, TweakState s, Tweak t)
    {
        var (text, kind) = s switch
        {
            TweakState.Applied => ("Applied", "good"),
            TweakState.NotApplied => ("Not applied", ""),
            TweakState.Partial => ("Partly applied", "warn"),
            _ => ("", ""),
        };
        pill.IsVisible = text.Length > 0;
        if (pill.Child is TextBlock tb) tb.Text = text;
        pill.Classes.Set("good", kind == "good");
        pill.Classes.Set("warn", kind == "warn");
        if (s == TweakState.Partial)
            ToolTip.SetTip(pill, "Some of this tweak's settings are in place and some aren't (changed by Windows, an update or another tool). Apply sets all of them.");
    }

    // ---------- Preferences ----------

    private void BuildPreferencesPage()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), Margin = new Thickness(0, 0, 14, 8) };
        var left = new StackPanel { Spacing = 8 };
        var right = new StackPanel { Spacing = 8 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var prefs = Catalog.Tweaks.Tweaks.Where(t => t.Group == "preference").ToList();
        for (var i = 0; i < prefs.Count; i++)
        {
            var t = prefs[i];
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
            if (RestartText(t.Restart) is { Length: > 0 } r && t.Restart != "explorer") title.Children.Add(Pill(r, "info"));
            text.Children.Add(title);
            text.Children.Add(new TextBlock { Text = t.Description, Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            g.Children.Add(text);

            Control control;
            if (t.IsChoice)
            {
                var box = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
                foreach (var c in t.Choices) box.Items.Add(new ComboBoxItem { Content = c.Name, [ToolTip.TipProperty] = c.Description });
                box.SelectionChanged += async (_, _) =>
                {
                    if (_refreshing || box.SelectedIndex < 0) return;
                    var c = t.Choices[box.SelectedIndex];
                    await RunJobAsync($"{t.Name}: {c.Name}", async (log, ct) =>
                    {
                        var ok = await Engine.ApplyChoiceAsync(t, c, log, ct);
                        Engine.FinishBatch([t], log);
                        await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
                        return (ok, ok ? $"{t.Name} set to {c.Name}." : null);
                    });
                };
                _choiceBoxes[t.Id] = box;
                control = box;
            }
            else
            {
                var sw = new ToggleSwitch { OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, -8, 0) };
                var partly = Pill("Mixed", "warn");
                partly.IsVisible = false;
                ToolTip.SetTip(partly, "Only some of this setting's values are in place. Switch it to set all of them.");
                _toggleStatus[t.Id] = partly;
                sw.IsCheckedChanged += async (_, _) =>
                {
                    if (_refreshing || sw.IsChecked is not { } on) return;
                    await RunJobAsync($"{t.Name}: {(on ? "on" : "off")}", async (log, ct) =>
                    {
                        var ok = await Engine.SetToggleAsync(t, on, log, ct);
                        var hint = Engine.FinishBatch([t], log);
                        await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
                        return (ok, ok ? $"{t.Name}: {(on ? "on" : "off")}. {hint}".Trim() : null);
                    });
                };
                _toggles[t.Id] = sw;
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                sp.Children.Add(partly);
                sp.Children.Add(sw);
                control = sp;
            }

            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            var card = Card(g, new Thickness(16, 11));
            (i % 2 == 0 ? left : right).Children.Add(card);
        }

        var reset = Btn("Windows defaults", tip: "Sets every preference on this page back to how Windows ships.");
        reset.Click += async (_, _) => await RunJobAsync("Resetting preferences to Windows defaults", async (log, ct) =>
        {
            var failed = 0;
            var all = Catalog.Tweaks.Tweaks.Where(t => t.Group == "preference").ToList();
            foreach (var t in all)
            {
                if (t.IsChoice)
                {
                    if (t.Choices.FirstOrDefault(c => c.Name == t.DefaultChoice) is { } c && !await Engine.ApplyChoiceAsync(t, c, log, ct)) failed++;
                }
                else if (!await Engine.SetToggleAsync(t, t.DefaultOn, log, ct)) failed++;
            }

            var hint = Engine.FinishBatch(all, log);
            await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
            return (failed == 0, failed == 0 ? $"Preferences are back to Windows' defaults. {hint}".Trim() : null);
        });
        _pages["Preferences"] = PageOf(Header("Preferences", "Each switch changes the setting straight away. Taskbar and Explorer ones restart Explorer for a moment.", reset),
            new ScrollViewer { Content = grid });
    }

    // ---------- Windows Update ----------

    private void BuildUpdatesPage()
    {
        var t = Catalog.Find("updates")!;
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*"), Margin = new Thickness(0, 0, 14, 0) };
        for (var i = 0; i < t.Choices.Count; i++)
        {
            var c = t.Choices[i];
            var sp = new StackPanel { Spacing = 10 };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            top.Children.Add(new TextBlock { Text = c.Name, FontSize = 20, FontWeight = FontWeight.Bold });
            var current = Pill("Current", "good");
            current.IsVisible = false;
            current.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(current);
            if (c.Name == "Security only") top.Children.Add(Pill("Recommended", "info"));
            sp.Children.Add(top);
            sp.Children.Add(new TextBlock { Text = c.Description, Classes = { "muted" }, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            if (c.Caution is { } caution) sp.Children.Add(Pill(caution, "bad"));
            var use = Btn("Use this", primary: c.Name == "Security only");
            use.HorizontalAlignment = HorizontalAlignment.Left;
            use.Click += async (_, _) => await RunJobAsync($"Windows Update: {c.Name}", async (log, ct) =>
            {
                var ok = await Engine.ApplyChoiceAsync(t, c, log, ct);
                Shell.BroadcastSettingsChange();
                await Dispatcher.UIThread.InvokeAsync(RefreshTweaksAsync);
                return (ok, ok ? $"Windows Update is set to {c.Name}." : null);
            });
            sp.Children.Add(use);
            var card = Card(sp, new Thickness(18));
            Grid.SetColumn(card, i * 2);
            cards.Children.Add(card);
            _updateCards[c.Name] = (use, current, card);
        }

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(_updateNow);
        body.Children.Add(cards);
        var more = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var settings = Btn("Open Windows Update settings");
        settings.Click += (_, _) => Shell.Open("ms-settings:windowsupdate");
        var fix = Btn("Reset Windows Update…", tip: "Features & fixes → Reset Windows Update");
        fix.Click += (_, _) => ShowPage("Features & fixes");
        more.Children.Add(settings);
        more.Children.Add(fix);
        body.Children.Add(more);
        _pages["Windows Update"] = PageOf(Header("Windows Update", "Choose how this PC gets updates. Any mode can be switched back at any time."), new ScrollViewer { Content = body });
    }
}
