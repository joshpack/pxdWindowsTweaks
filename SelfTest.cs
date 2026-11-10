using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using pxdWindowsTweaks.Core;
using pxdWindowsTweaks.Views;

namespace pxdWindowsTweaks;

/// <summary>
/// --self-test DIR [--sample]: checks the catalogs, reads every tweak's state on this PC (read only), applies and
/// undoes every tweak, preference and choice against a scratch registry key with services and scripts faked (the real
/// settings are never touched), runs two harmless PowerShell scripts, checks profiles, the output filter, window
/// placement and the single-instance hand-off, and renders every page to a PNG on the headless platform. Nothing is
/// shown on screen. With --sample the pages show made-up data, for README screenshots. Exit code 0 = all passed.
/// </summary>
internal static class SelfTest
{
    public static string? Dir { get; set; }
    public static bool Sample { get; set; }
    public static int ExitCode { get; private set; } = 1;

    private static readonly StringBuilder R = new();
    private static int _failed;

    private static void Check(string name, bool ok, string? detail = null)
    {
        R.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : ": " + detail)}");
        if (!ok) _failed++;
    }

    public static async Task RunAsync()
    {
        R.AppendLine($"pxd Windows Tweaks {typeof(SelfTest).Assembly.GetName().Version?.ToString(3)} self-test, {(Sample ? "sample data" : "this PC")}, {DateTime.Now:yyyy-MM-dd HH:mm}");
        R.AppendLine($"Elevated: {Elevation.IsElevated}");
        R.AppendLine();
        try
        {
            CatalogChecks();
            if (!Sample) RealStates();
            await RoundTrip();
            await Scripts();
            Profiles();
            OutputFilter();
            await Render();
            await Placement();
            SingleInstanceCheck();
            await MigrationChecks();
            await UpdateChecks();
        }
        catch (Exception ex)
        {
            Check("no crash", false, ex.ToString());
        }

        R.AppendLine();
        R.AppendLine(_failed == 0 ? "RESULT: all checks passed" : $"RESULT: {_failed} check(s) FAILED");
        ExitCode = _failed == 0 ? 0 : 1;
        await File.WriteAllTextAsync(Path.Combine(Dir!, "report.txt"), R.ToString());
    }

    // ---------- Updating before start ----------

    /// <summary>pxdUpdate: skipped for --self-test and dev builds; a fake release on a local HttpListener updates a temp folder.</summary>
    private static async Task UpdateChecks()
    {
        R.AppendLine("== Update before start (local fake release, temp folders) ==");
        foreach (var (name, ok, detail) in await SelfTestUpdate.RunAsync(Dir!))
            Check(name, ok, detail.Length > 0 ? detail : null);
        R.AppendLine();
    }

    // ---------- Migration from the former name (temp folders and files only) ----------

    private static async Task MigrationChecks()
    {
        R.AppendLine("== Migration from the former name (temp folders, faked task scheduler) ==");
        var root = Path.Combine(Dir!, "migrate");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        var former = pxdStandard.pxdMigrate.FormerName(Program.AppId);
        var oldDir = Path.Combine(root, "appdata", former);
        var newDir = Path.Combine(root, "appdata", Program.AppId);
        Directory.CreateDirectory(Path.Combine(oldDir, "undo"));
        Directory.CreateDirectory(Path.Combine(oldDir, "logs"));
        var rec = new UndoRecord
        {
            Id = "telemetry", AppliedAt = new DateTime(2026, 11, 7, 12, 0, 0),
            Values = [new SavedValue { Path = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", Name = "AllowTelemetry", Exists = true, Kind = "DWord", Data = "3" }],
            Services = [new SavedService { Name = "DiagTrack", Start = "Automatic" }],
        };
        File.WriteAllText(Path.Combine(oldDir, "undo", "telemetry.json"), System.Text.Json.JsonSerializer.Serialize(rec, Catalog.Json));
        File.WriteAllText(Path.Combine(oldDir, "logs", "apply-old.log"), "old log");
        File.WriteAllText(Path.Combine(oldDir, "windows.json"), """{"main":{"X":1}}""");

        var moved = Migration.DataFolder(Path.Combine(root, "appdata"));
        var env = Environment.GetEnvironmentVariable("PXDWT_DATA");
        UndoRecord? back;
        try
        {
            Environment.SetEnvironmentVariable("PXDWT_DATA", newDir);
            back = Engine.LoadUndo("telemetry");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PXDWT_DATA", env);
        }

        Check("Data folder moved to the new name (undo, logs, placement)", moved && !Directory.Exists(oldDir)
            && File.Exists(Path.Combine(newDir, "logs", "apply-old.log")) && File.Exists(Path.Combine(newDir, "windows.json")));
        Check("Undo records from the former version are found after the move", back is { Id: "telemetry" } && back.Values is [{ Data: "3" }] && back.Services is [{ Start: "Automatic" }]);
        File.WriteAllText(Path.Combine(newDir, "windows.json"), """{"main":{"X":2}}""");
        Check("Data folder: second run is a no-op", !Migration.DataFolder(Path.Combine(root, "appdata")) && File.ReadAllText(Path.Combine(newDir, "windows.json")).Contains("\"X\":2"));

        // Registry backup task: Windows' task store is faked by a folder, the script by Shell.Fake.
        var tasks = Path.Combine(root, "Tasks");
        var brand = pxdStandard.pxdMigrate.FormerPrefix.ToUpperInvariant();
        Directory.CreateDirectory(Path.Combine(tasks, brand));
        File.WriteAllText(Path.Combine(tasks, brand, brand + " Registry Backup"), "<Task />");
        var ran = new List<string>();
        Shell.Fake = s => { lock (ran) ran.Add(s); return 0; };
        try
        {
            var limited = await Migration.RegistryBackupTaskAsync(tasks, elevated: false);
            Check("Task: left alone without administrator rights", !limited && ran.Count == 0);
            var task = await Migration.RegistryBackupTaskAsync(tasks, elevated: true);
            Check("Task: moved when elevated (register new from the old definition, remove old)", task && ran.Count == 1
                && ran[0].Contains(@"-TaskPath '\pxd\' -TaskName 'pxd Registry Backup' -Xml") && ran[0].Contains("Unregister-ScheduledTask -TaskPath $oldPath"));
            File.Delete(Path.Combine(tasks, brand, brand + " Registry Backup"));
            ran.Clear();
            Check("Task: second run (old task gone) is a no-op", !await Migration.RegistryBackupTaskAsync(tasks, elevated: true) && ran.Count == 0);
        }
        finally
        {
            Shell.Fake = null;
        }

        // The edited scripts parse (Windows PowerShell's parser; nothing is run).
        var parseDir = Path.Combine(root, "parse");
        Directory.CreateDirectory(parseDir);
        File.WriteAllText(Path.Combine(parseDir, "task-migration.ps1"), Migration.TaskScript);
        foreach (var n in new[] { "adobe-block.on.ps1", "adobe-block.off.ps1", "fix-registry-backup.ps1" })
            File.WriteAllText(Path.Combine(parseDir, n), Catalog.Script(n)!);
        var lines = new List<string>();
        var code = await Shell.RunScriptAsync(
            $"foreach ($f in Get-ChildItem '{parseDir}' -Filter *.ps1) {{ $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e); if ($e) {{ Write-Host \"$($f.Name): $($e[0].Message)\" }} else {{ Write-Host \"ok $($f.Name)\" }} }}",
            l => lines.Add(l));
        Check("Task migration and edited scripts parse", code == 0 && lines.Count(l => l.StartsWith("ok ")) == 4, string.Join(" | ", lines));

        // Adobe hosts block written by the former version: a temp file stands in for the hosts file.
        var hosts = Path.Combine(root, "hosts");
        var oldBlock = $"\r\n# {brand} Windows Tweaks: Adobe block start\r\n0.0.0.0 example.adobe.test\r\n# {brand} Windows Tweaks: Adobe block end\r\n";
        string? Redirected(string name)
        {
            var s = Catalog.Script(name)!;
            const string real = @"Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'";
            if (!s.Contains(real)) return null;   // never run against the real hosts file
            return s.Replace(real, $"'{hosts}'").Replace("ipconfig /flushdns | Out-Null", "")
                .Replace("Invoke-RestMethod", "throw 'no download in the self-test'; Invoke-RestMethod");
        }

        File.WriteAllText(hosts, "127.0.0.1 localhost" + oldBlock);
        var on = Redirected("adobe-block.on.ps1");
        lines.Clear();
        code = on is null ? -1 : await Shell.RunScriptAsync(on, l => lines.Add(l));
        var text = File.ReadAllText(hosts);
        Check("Adobe block from the former version counts as applied (markers renamed, nothing downloaded)",
            code == 0 && !text.Contains(brand + " Windows") && text.Contains("# pxd Windows Tweaks: Adobe block start") && text.Contains("example.adobe.test"),
            $"exit {code}: {string.Join(" | ", lines)}");

        File.WriteAllText(hosts, "127.0.0.1 localhost" + oldBlock);
        var off = Redirected("adobe-block.off.ps1");
        lines.Clear();
        code = off is null ? -1 : await Shell.RunScriptAsync(off, l => lines.Add(l));
        text = File.ReadAllText(hosts);
        Check("Undo removes an Adobe block written by the former version", code == 0 && text == "127.0.0.1 localhost", $"exit {code}: {string.Join(" | ", lines)}");
        R.AppendLine();
    }

    // ---------- Catalog ----------

    private static void CatalogChecks()
    {
        R.AppendLine("== Catalog ==");
        var all = Catalog.Tweaks.Tweaks;
        var groups = all.GroupBy(t => t.Group).Select(g => $"{g.Key} {g.Count()}");
        Check($"{all.Count} tweaks loaded ({string.Join(", ", groups)})", all.Count >= 60);
        var dupes = all.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check("Tweak ids are unique", dupes.Count == 0, string.Join(", ", dupes));
        Check("Every tweak has a name and description", all.All(t => t.Name.Length > 0 && t.Description.Length > 10),
            string.Join(", ", all.Where(t => t.Name.Length == 0 || t.Description.Length <= 10).Select(t => t.Id)));
        string[] groupsOk = ["essential", "advanced", "preference", "updates"];
        Check("Groups are known", all.All(t => groupsOk.Contains(t.Group)));
        string[] types = ["DWord", "QWord", "String", "ExpandString", "Binary", "MultiString"];
        var regs = all.SelectMany(t => t.Registry.Concat(t.Choices.SelectMany(c => c.Registry)).Select(r => (t.Id, r))).ToList();
        var badPaths = regs.Where(x => !Reg.IsValidPath(x.r.Path) || !types.Contains(x.r.Type)).Select(x => $"{x.Id}:{x.r.Path}").ToList();
        Check($"{regs.Count} registry values have valid hives and types", badPaths.Count == 0, string.Join(", ", badPaths));
        var badValues = regs.Where(x => !ValueParses(x.r.Type, x.r.On) || !ValueParses(x.r.Type, x.r.Off) || !ValueParses(x.r.Type, x.r.Value)).Select(x => $"{x.Id}:{x.r.Name}").ToList();
        Check("Registry data fits its type", badValues.Count == 0, string.Join(", ", badValues));
        string[] starts = ["Automatic", "AutomaticDelayed", "Manual", "Disabled"];
        var svcs = all.SelectMany(t => t.Services.Concat(t.Choices.SelectMany(c => c.Services))).ToList();
        Check($"{svcs.Count} service settings use known start types",
            svcs.All(s => new[] { s.On, s.Off, s.Start }.All(v => v is null || starts.Contains(v))));
        Check("Toggles all have on and off values", all.Where(t => t.IsToggle).All(t => t.Registry.Count > 0 && t.Registry.All(r => r.On != r.Off)),
            string.Join(", ", all.Where(t => t.IsToggle).Where(t => t.Registry.Any(r => r.On == r.Off)).Select(t => t.Id)));
        Check("Choices have a default that exists", all.Where(t => t.IsChoice).All(t => t.Choices.Any(c => c.Name == t.DefaultChoice)));
        var actionsWithoutWork = all.Where(t => t.IsAction && t.OnScript is null && t.Registry.Count == 0).Select(t => t.Id).ToList();
        Check("Every one-off action has a script", actionsWithoutWork.Count == 0, string.Join(", ", actionsWithoutWork));
        var scriptOnly = all.Where(t => !t.IsAction && !t.IsChoice && t.Registry.Count == 0 && t.Services.Count == 0 && t.OnScript is null).Select(t => t.Id).ToList();
        Check("Every tweak changes something", scriptOnly.Count == 0, string.Join(", ", scriptOnly));
        var noUndo = all.Where(t => !t.IsAction && !t.IsChoice && t.Registry.Count == 0 && t.Services.Count == 0 && t.OffScript is null).Select(t => t.Id).ToList();
        Check("Every script-only tweak has an undo script", noUndo.Count == 0, string.Join(", ", noUndo));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all)
        {
            used.Add($"{t.Id}.on.ps1");
            used.Add($"{t.Id}.off.ps1");
            foreach (var c in t.Choices) used.Add($"{t.Id}.{c.Slug}.ps1");
        }

        foreach (var f in SystemTools.Fixes) used.Add(f.Script);
        used.Add("power-ultimate-on.ps1");
        used.Add("power-ultimate-off.ps1");
        var names = Catalog.ScriptNames().ToList();
        var unused = names.Where(n => !used.Contains(n)).ToList();
        Check($"{names.Count} embedded scripts, all used", unused.Count == 0, string.Join(", ", unused));
        var missingFix = SystemTools.Fixes.Where(f => Catalog.Script(f.Script) is null).Select(f => f.Script).ToList();
        Check("Every fix has its script", missingFix.Count == 0, string.Join(", ", missingFix));
        var nonAscii = names.Where(n => Catalog.Script(n)!.Any(c => c > 127)).ToList();
        Check("Scripts are plain ASCII", nonAscii.Count == 0, string.Join(", ", nonAscii));

        foreach (var (name, ids) in Catalog.Tweaks.Presets)
        {
            var missing = ids.Where(id => Catalog.Find(id) is null).ToList();
            Check($"Preset {name}: {ids.Count} tweaks, all exist", missing.Count == 0, string.Join(", ", missing));
        }

        var apps = Catalog.Apps;
        Check($"{apps.Count} installable apps in {apps.Select(a => a.Category).Distinct().Count()} categories", apps.Count > 200);
        var dupApps = apps.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check("App ids are unique", dupApps.Count == 0, string.Join(", ", dupApps));
        Check("App ids look like winget ids", apps.All(a => a.Id.Length > 2 && !a.Id.Contains(' ')));
        Check($"{SystemTools.Bloat.Length} removable built-in apps, unique", SystemTools.Bloat.Select(b => b.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count() == SystemTools.Bloat.Length);
        R.AppendLine();
    }

    private static bool ValueParses(string type, string? v)
    {
        if (v is null) return true;
        return type switch
        {
            "DWord" => uint.TryParse(v, out _),
            "QWord" => ulong.TryParse(v, out _),
            "Binary" => v.Length % 2 == 0 && v.All(Uri.IsHexDigit),
            _ => true,
        };
    }

    // ---------- This PC, read only ----------

    private static void RealStates()
    {
        R.AppendLine("== This PC (read only) ==");
        var clock = Stopwatch.StartNew();
        var states = Catalog.Tweaks.Tweaks.Where(t => !t.IsChoice).Select(t => (t, s: Engine.State(t))).ToList();
        Check($"Read {states.Count} states in {clock.ElapsedMilliseconds} ms", clock.ElapsedMilliseconds < 5000);
        foreach (var g in states.GroupBy(x => x.s))
            R.AppendLine($"    {g.Key}: {string.Join(", ", g.Select(x => x.t.Id))}");
        foreach (var t in Catalog.Tweaks.Tweaks.Where(t => t.IsChoice))
            R.AppendLine($"    {t.Name}: {Engine.CurrentChoice(t)?.Name ?? "custom"}");
        R.AppendLine($"    winget: {Winget.ExePath() ?? "not found"}");
        R.AppendLine();
    }

    // ---------- Apply and undo, on a scratch key ----------

    private static async Task RoundTrip()
    {
        R.AppendLine("== Apply / undo (scratch registry key, faked services and scripts) ==");
        var scratch = $@"Software\pxdWindowsTweaksSelfTest\{Guid.NewGuid():N}";
        var scripts = new List<string>();
        Reg.Redirect = scratch;
        Shell.Fake = s =>
        {
            lock (scripts) scripts.Add(s);
            return 0;
        };
        var svc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Catalog.Tweaks.Tweaks.SelectMany(t => t.Services.Concat(t.Choices.SelectMany(c => c.Services))))
            svc[s.Name] = "Manual";
        svc["DiagTrack"] = "Automatic";
        Services.Fake = svc;
        var undoDir = Engine.UndoFolder;
        try
        {
            if (Directory.Exists(undoDir)) Directory.Delete(undoDir, true);
            void Nop(string _) { }

            // This PC's own values come back, not Windows' defaults.
            Reg.Write(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", "DWord", "3");
            var tel = Catalog.Find("telemetry")!;
            Check("Telemetry starts partly applied (seeded values)", Engine.State(tel) is TweakState.NotApplied or TweakState.Partial, Engine.State(tel).ToString());
            await Engine.ApplyAsync(tel, Nop);
            Check("Telemetry applied", Engine.State(tel) == TweakState.Applied, Engine.State(tel).ToString());
            Check("Undo record written", File.Exists(Path.Combine(undoDir, "telemetry.json")));
            Check("Services changed (DiagTrack, WerSvc disabled)", svc["DiagTrack"] == "Disabled" && svc["WerSvc"] == "Disabled");
            Check("Telemetry's extra script ran", scripts.Any(s => s.Contains("SubmitSamplesConsent 2")));
            await Engine.UndoAsync(tel, Nop);
            var allow = Reg.Read(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry");
            Check("Undo restores this PC's own value (AllowTelemetry 3, not removed)", allow.Data == "3", allow.ToString());
            Check("Undo restores services (DiagTrack Automatic, WerSvc Manual)", svc["DiagTrack"] == "Automatic" && svc["WerSvc"] == "Manual", $"{svc["DiagTrack"]}, {svc["WerSvc"]}");
            Check("Undo record removed", !File.Exists(Path.Combine(undoDir, "telemetry.json")));

            var failures = new List<string>();
            foreach (var t in Catalog.Tweaks.Tweaks.Where(t => !t.IsChoice && t.Probe is null))
            {
                var before = t.Registry.Select(r => (r, Reg.Read(r.Path, r.Name))).ToList();
                var ok = await Engine.ApplyAsync(t, Nop);
                var s = Engine.State(t);
                var expected = t.Registry.Count + t.Services.Count == 0 ? (t.IsAction ? TweakState.Action : TweakState.Applied)
                    : t.IsAction && t.OffScript is null ? TweakState.Action : TweakState.Applied;
                if (!ok || s != expected) failures.Add($"{t.Id} apply→{s}");
                if (t.CanUndo)
                {
                    ok = await Engine.UndoAsync(t, Nop);
                    var after = before.Where(b => !t.IsToggle).Where(b => Reg.Read(b.r.Path, b.r.Name) != b.Item2).Select(b => b.r.Name).ToList();
                    if (!ok || after.Count > 0) failures.Add($"{t.Id} undo left {string.Join("/", after)}");
                    if (t.IsToggle && Engine.State(t) != TweakState.NotApplied) failures.Add($"{t.Id} off→{Engine.State(t)}");
                }
            }

            Check($"Every tweak and preference applies and undoes cleanly ({Catalog.Tweaks.Tweaks.Count(t => !t.IsChoice)})", failures.Count == 0, string.Join("; ", failures));

            foreach (var t in Catalog.Tweaks.Tweaks.Where(t => t.IsChoice))
            foreach (var c in t.Choices)
            {
                await Engine.ApplyChoiceAsync(t, c, Nop);
                var now = Engine.CurrentChoice(t);
                Check($"{t.Name}: {c.Name} is read back", now?.Name == c.Name, now?.Name ?? "none");
            }

            Check("Choice scripts ran (Windows Update Off stops the services)", scripts.Any(s => s.Contains("Disable-ScheduledTask")));

            // A tweak whose key was changed by something else shows as partly applied.
            var act = Catalog.Find("activity-history")!;
            await Engine.ApplyAsync(act, Nop);
            Reg.Write(act.Registry[0].Path, act.Registry[0].Name, "DWord", null);
            Check("Partly applied is detected", Engine.State(act) == TweakState.Partial, Engine.State(act).ToString());
            await Engine.UndoAsync(act, Nop);
        }
        finally
        {
            Reg.Redirect = null;
            Shell.Fake = null;
            Services.Fake = null;
            // Only the exact key this test made.
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\pxdWindowsTweaksSelfTest", true);
            root?.DeleteSubKeyTree(scratch.Split('\\').Last(), false);
            var empty = root is not null && root.SubKeyCount == 0;
            root?.Dispose();
            if (empty) Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\pxdWindowsTweaksSelfTest", false);
        }

        Check("Scratch registry key removed", Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{scratch}") is null);
        R.AppendLine();
    }

    // ---------- PowerShell ----------

    private static async Task Scripts()
    {
        R.AppendLine("== PowerShell runner (two harmless scripts, no window) ==");
        var lines = new List<string>();
        var code = await Shell.RunScriptAsync("Write-Host 'hello from pxd'; Write-Output ([Environment]::Is64BitProcess)", l => lines.Add(l));
        Check("Script runs and its output is read", code == 0 && lines.Contains("hello from pxd") && lines.Contains("True"), $"exit {code}: {string.Join(" | ", lines)}");
        lines.Clear();
        code = await Shell.RunScriptAsync("throw 'expected failure'", l => lines.Add(l));
        Check("A failing script reports a non-zero exit and the error", code != 0 && lines.Any(l => l.Contains("expected failure")), $"exit {code}: {string.Join(" | ", lines)}");
        R.AppendLine();
    }

    // ---------- Profiles ----------

    private static void Profiles()
    {
        R.AppendLine("== Selection files ==");
        var file = Path.Combine(Dir!, "profile.json");
        var p = new TweakProfile { Tweaks = ["telemetry", "wpbt"], Preferences = { ["dark-mode"] = true }, Choices = { ["updates"] = "Security only" }, Apps = ["Mozilla.Firefox"], RemoveApps = ["Microsoft.BingNews"], Features = ["wsl"] };
        p.Save(file);
        var q = TweakProfile.Load(file);
        Check("Save and load keep everything", q.Tweaks.SequenceEqual(p.Tweaks) && q.Preferences["dark-mode"] && q.Choices["updates"] == "Security only"
                                               && q.Apps.Single() == "Mozilla.Firefox" && q.RemoveApps.Single() == "Microsoft.BingNews" && q.Features.Single() == "wsl");
        R.AppendLine();
    }

    private static void OutputFilter()
    {
        R.AppendLine("== Output filter ==");
        Check("Spinners dropped", Shell.Clean("  \\ ") == "" && Shell.Clean("-") == "");
        Check("Progress bars dropped", Shell.Clean("  ██████████████▒▒▒▒▒▒  12.0 MB / 20.0 MB") == "");
        Check("Carriage-return overwrites keep the last text", Shell.Clean("10%\r50%\rSuccessfully installed") == "Successfully installed");
        Check("Normal lines kept", Shell.Clean("Found Mozilla Firefox [Mozilla.Firefox]") == "Found Mozilla Firefox [Mozilla.Firefox]");
        Check("winget codes", Winget.Succeeded(0) && Winget.Succeeded(unchecked((int)0x8A15002B)) && !Winget.Succeeded(1));
        R.AppendLine();
    }

    // ---------- Window ----------

    private static async Task Render()
    {
        R.AppendLine("== Window (headless renderer: nothing is shown on screen) ==");
        if (Sample)
        {
            Winget.FakeInstalled = ["Mozilla.Firefox", "Valve.Steam", "Discord.Discord", "7zip.7zip", "VideoLAN.VLC", "Microsoft.PowerToys", "Notepad++.Notepad++", "Git.Git"];
            SystemTools.FakeInstalledAppx = new HashSet<string>(SystemTools.Bloat.Where((b, i) => i % 3 != 2).Select(b => b.Package), StringComparer.OrdinalIgnoreCase);
            SystemTools.FakeFeatureStates = new Dictionary<string, string>
            {
                ["NetFx3"] = "Enabled", ["NetFx4-AdvSrvs"] = "Enabled", ["Microsoft-Windows-Subsystem-Linux"] = "Enabled", ["VirtualMachinePlatform"] = "Enabled",
                ["Microsoft-Hyper-V-All"] = "Disabled", ["Containers-DisposableClientVM"] = "Disabled", ["WindowsMediaPlayer"] = "Disabled", ["MediaPlayback"] = "Enabled",
                ["DirectPlay"] = "Disabled", ["LegacyComponents"] = "Disabled", ["ServicesForNFS-ClientOnly"] = "Disabled", ["ClientForNFS-Infrastructure"] = "Disabled",
                ["NFS-Administration"] = "Disabled", ["TelnetClient"] = "Disabled", ["SMB1Protocol-Client"] = "Disabled",
            };
            SystemTools.FakeDns = [new AdapterDns("Ethernet", "1.1.1.1, 1.0.0.1, 2606:4700:4700::1111"), new AdapterDns("Wi-Fi", "192.168.1.1")];
        }
        else
        {
            // This PC's real lists, read only; winget export is slow, so the installed list is faked.
            Winget.FakeInstalled = [];
        }

        var window = new MainWindow(elevated: Sample ? true : null, autoLoad: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-30000, -30000),
            ShowActivated = false,
            ShowInTaskbar = false,
            SelfTestMode = true,
        };
        if (Sample)
        {
            var applied = new[] { "activity-history", "consumer-features", "wpbt", "telemetry", "delivery-optimization", "end-task", "device-companion-apps", "dark-mode", "show-extensions", "show-hidden", "game-mode", "long-paths", "mouse-acceleration" };
            window.FakeStates = Catalog.Tweaks.Tweaks.Where(t => !t.IsChoice).ToDictionary(t => t.Id,
                t => t.IsAction ? TweakState.Action : applied.Contains(t.Id) ? TweakState.Applied : t.Id == "location" ? TweakState.Partial
                    : t.IsToggle ? (t.DefaultOn && t.Id != "mouse-acceleration" && t.Id != "bing-search" && t.Id != "start-recommendations" ? TweakState.Applied : TweakState.NotApplied) : TweakState.NotApplied);
            window.LoadSelection(new TweakProfile { Tweaks = Catalog.Tweaks.Presets["Standard"], Apps = ["Brave.Brave", "Discord.Discord", "OBSProject.OBSStudio"], RemoveApps = ["Microsoft.BingNews", "Microsoft.BingWeather", "Clipchamp.Clipchamp", "Microsoft.Copilot"] });
        }

        window.Show();
        await window.RefreshTweaksAsync();
        await window.LoadInstalledAppsAsync();
        await window.LoadAppxAsync();
        await window.LoadFeaturesAsync();
        await window.LoadNetworkAsync();
        if (Sample)
        {
            window.Log("── 09:41:12 Applying 13 tweak(s) ──");
            window.Log("▶ Create a restore point");
            window.Log("  Restore point created.");
            window.Log("  done");
            window.Log("▶ Turn off Activity History");
            window.Log(@"  HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableActivityFeed = 1");
            window.Log(@"  HKLM\SOFTWARE\Policies\Microsoft\Windows\System\PublishUserActivities = 0");
            window.Log("  done");
            window.Log("▶ Trim background services");
            window.Log("  service CscService: Disabled");
            window.Log("  service DiagTrack: Disabled");
            window.Log("  done");
            window.Log("── finished in 21.4 s ──");
            window.ShowNotice("Applied 13 tweak(s). Restart the PC to finish some of these changes.", good: true);
        }

        string[] files = ["tweaks", "preferences", "debloat", "install", "features", "updates", "network", "log"];
        for (var i = 0; i < MainWindow.PageNames.Length; i++)
        {
            window.ShowPage(MainWindow.PageNames[i]);
            window.Width = 1180;
            window.Height = 780;
            window.UpdateLayout();
            await Task.Delay(120);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bmp = new RenderTargetBitmap(new PixelSize(1180, 780));
            bmp.Render(window);
            var file = Path.Combine(Dir!, files[i] + ".png");
            bmp.Save(file, PngBitmapEncoderOptions.Default);
            Check($"Rendered {MainWindow.PageNames[i]}", new FileInfo(file).Length > 10_000, file);
        }

        var sel = window.CurrentSelection();
        if (Sample) Check("Ticked items become a selection", sel.Tweaks.Count == Catalog.Tweaks.Presets["Standard"].Count && sel.Apps.Count == 3, $"{sel.Tweaks.Count} tweaks, {sel.Apps.Count} apps");
        var all = window.GetLogicalDescendants().ToList();
        Check("Title bar: icon, name, three caption buttons", window.WindowDecorations == WindowDecorations.BorderOnly
                                                                && all.OfType<TextBlock>().Any(t => t.Text == window.Title)
                                                                && all.OfType<Button>().Count(b => b.Classes.Contains("pxd-caption")) == 3);
        window.Close();
        Winget.FakeInstalled = null;
        SystemTools.FakeInstalledAppx = null;
        SystemTools.FakeFeatureStates = null;
        SystemTools.FakeDns = null;
        R.AppendLine();
    }

    private static MainWindow OffScreen() => new(elevated: true, autoLoad: false)
    {
        WindowStartupLocation = WindowStartupLocation.Manual,
        Position = new PixelPoint(-30000, -30000),
        ShowActivated = false,
        ShowInTaskbar = false,
        SelfTestMode = true,
    };

    private static async Task Placement()
    {
        R.AppendLine("== Window placement (windows.json in the test data folder) ==");
        const string key = "selftest";
        var file = Path.Combine(Program.DataFolder, "windows.json");
        if (File.Exists(file)) File.Delete(file);
        var a = OffScreen();
        pxdStandard.pxdChrome.RememberPlacement(a, key);
        a.Show();
        await Task.Delay(50);
        a.Width = 1000;
        a.Height = 640;
        a.Position = new PixelPoint(-29000, -29000);
        a.UpdateLayout();
        await Task.Delay(50);
        a.Close();
        var json = File.Exists(file) ? await File.ReadAllTextAsync(file) : "";
        Check("Placement saved", json.Contains("\"selftest\"") && json.Contains("\"Width\":1000") && json.Contains("\"Height\":640"), json);
        var b = OffScreen();
        pxdStandard.pxdChrome.RememberPlacement(b, key);
        Check("Placement restored (size), off-screen spot ignored", b.Width == 1000 && b.Height == 640 && b.Position == new PixelPoint(-30000, -30000), $"{b.Width}x{b.Height} at {b.Position}");
        R.AppendLine();
    }

    private static void SingleInstanceCheck()
    {
        R.AppendLine("== Single instance ==");
        var name = "pxdWindowsTweaks-selftest-" + Guid.NewGuid().ToString("N")[..8];
        string[]? got = null;
        using var done = new ManualResetEventSlim();
        using (SingleInstance.Listen(name, args => { got = args; done.Set(); }))
        {
            var sent = SingleInstance.Send(name, ["--from-second-start"]);
            done.Wait(3000);
            Check("Second start reaches the running copy", sent && got is ["--from-second-start"], got is null ? "nothing received" : string.Join("|", got));
        }

        R.AppendLine();
    }
}
