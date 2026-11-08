using System.Text.Json;

namespace PaxWindowsTweaks.Core;

public enum TweakState
{
    /// <summary>Can't be read (script-only tweak never applied from here).</summary>
    Unknown,
    Applied,
    NotApplied,
    Partial,
    /// <summary>One-shot action: nothing to show.</summary>
    Action,
}

/// <summary>What was there before a tweak ran, so Undo puts back this PC's own values rather than a guess.</summary>
public sealed class UndoRecord
{
    public string Id { get; set; } = "";
    public DateTime AppliedAt { get; set; }
    public List<SavedValue> Values { get; set; } = [];
    public List<SavedService> Services { get; set; } = [];
}

public sealed class SavedValue
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Exists { get; set; }
    public string? Kind { get; set; }
    public string? Data { get; set; }
}

public sealed class SavedService
{
    public string Name { get; set; } = "";
    public string Start { get; set; } = "";
}

/// <summary>Reads tweak states and applies, undoes and switches them.</summary>
public static class Engine
{
    /// <summary>%APPDATA%\PaxWindowsTweaks\undo (or under PAXWT_DATA).</summary>
    public static string UndoFolder => Path.Combine(Program.DataFolder, "undo");

    // ---------- State ----------

    public static TweakState State(Tweak t)
    {
        if (t.IsChoice) return TweakState.Unknown;
        if (t.Probe is { } probe && ProbeState(probe) is { } p) return p;

        int on = 0, off = 0, total = 0;
        foreach (var r in t.Registry)
        {
            total++;
            var s = Reg.Read(r.Path, r.Name);
            var isOn = Reg.Matches(s, r.On, r.Type);
            if (isOn) on++;
            else if (Reg.Matches(s, r.Off, r.Type) || (!s.Exists && !t.IsToggle)) off++;
        }

        foreach (var sv in t.Services)
        {
            var actual = Services.StartType(sv.Name);
            if (actual is null) continue;   // not on this PC
            total++;
            if (Services.Matches(actual, sv.On)) on++;
            else off++;
        }

        if (total == 0)
        {
            if (t.IsAction) return TweakState.Action;
            return LoadUndo(t.Id) is not null ? TweakState.Applied : TweakState.Unknown;
        }

        if (t.IsAction && t.OffScript is null) return TweakState.Action;
        if (on == total) return TweakState.Applied;
        if (on == 0) return TweakState.NotApplied;
        return TweakState.Partial;
    }

    /// <summary>The choice that matches the registry now, or null when none does exactly.</summary>
    public static Choice? CurrentChoice(Tweak t)
    {
        foreach (var c in t.Choices)
        {
            var match = c.Registry.All(r => Reg.Matches(Reg.Read(r.Path, r.Name), r.Value, r.Type))
                        && c.Services.All(s => Services.Matches(Services.StartType(s.Name), s.Start));
            if (match) return c;
        }

        return null;
    }

    /// <summary>States that can't be read from the catalog's registry values.</summary>
    private static TweakState? ProbeState(string probe)
    {
        try
        {
            switch (probe)
            {
                case "svchost-split":
                {
                    var v = Reg.Read(@"HKLM\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB");
                    if (!v.Exists || !ulong.TryParse(v.Data, out var kb)) return TweakState.NotApplied;
                    var ramKb = (ulong)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024);
                    return kb >= ramKb * 9 / 10 ? TweakState.Applied : TweakState.NotApplied;
                }
                case "store-search":
                {
                    var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db");
                    if (!File.Exists(db)) return TweakState.NotApplied;
                    return HasEveryoneDeny(new FileInfo(db).GetAccessControl()) ? TweakState.Applied : TweakState.NotApplied;
                }
                case "logi-block":
                {
                    var pf = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var dir = new DirectoryInfo(Path.Combine(pf, "LogiDownloadAssistant"));
                    if (!dir.Exists) return TweakState.NotApplied;
                    return HasEveryoneDeny(dir.GetAccessControl()) ? TweakState.Applied : TweakState.NotApplied;
                }
            }
        }
        catch
        {
            // fall through: unknown
        }

        return null;
    }

    private static bool HasEveryoneDeny(System.Security.AccessControl.FileSystemSecurity acl)
    {
        var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        foreach (System.Security.AccessControl.FileSystemAccessRule rule in acl.GetAccessRules(true, false, typeof(System.Security.Principal.SecurityIdentifier)))
            if (rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny && rule.IdentityReference.Equals(everyone))
                return true;
        return false;
    }

    // ---------- Changes ----------

    /// <summary>Applies a tweak (or turns a preference on). Returns false if part of it failed.</summary>
    public static async Task<bool> ApplyAsync(Tweak t, Action<string> log, CancellationToken ct = default)
    {
        log($"▶ {t.Name}");
        var ok = true;
        // Remember what's there first, unless an earlier apply already did (keep the oldest, truest values).
        if (!t.IsToggle && (t.Registry.Count > 0 || t.Services.Count > 0) && LoadUndo(t.Id) is null)
            SaveUndo(Snapshot(t));

        ok &= WriteRegistry(t.Registry.Select(r => (r, r.On)), log);
        foreach (var s in t.Services.Where(s => s.On is not null))
            Services.Set(s.Name, s.On!, log);

        if (t.OnScript is { } script)
            ok &= await RunScript(script, log, ct);

        if (!t.IsToggle && t.Registry.Count == 0 && t.Services.Count == 0 && t.OffScript is not null)
            SaveUndo(new UndoRecord { Id = t.Id, AppliedAt = DateTime.Now });   // marks script-only tweaks as applied

        log(ok ? "  done" : "  finished with errors");
        return ok;
    }

    /// <summary>Undoes a tweak (or turns a preference off): this PC's recorded values when there are any, else Windows' defaults.</summary>
    public static async Task<bool> UndoAsync(Tweak t, Action<string> log, CancellationToken ct = default)
    {
        log($"↶ {t.Name}");
        var ok = true;
        var saved = t.IsToggle ? null : LoadUndo(t.Id);
        if (saved is not null && saved.Values.Count > 0)
        {
            foreach (var v in saved.Values)
            {
                try
                {
                    Reg.Restore(v.Path, v.Name, new RegState(v.Exists, v.Kind, v.Data));
                    log($"  {v.Path}\\{v.Name}: {(v.Exists ? "restored" : "removed")}");
                }
                catch (Exception ex)
                {
                    ok = false;
                    log($"  {v.Path}\\{v.Name}: couldn't restore ({ex.Message})");
                }
            }
        }
        else
        {
            ok &= WriteRegistry(t.Registry.Select(r => (r, r.Off)), log);
        }

        if (saved is not null && saved.Services.Count > 0)
            foreach (var s in saved.Services) Services.Set(s.Name, s.Start, log);
        else
            foreach (var s in t.Services.Where(s => s.Off is not null)) Services.Set(s.Name, s.Off!, log);

        if (t.OffScript is { } script)
            ok &= await RunScript(script, log, ct);

        if (ok) DeleteUndo(t.Id);
        log(ok ? "  undone" : "  finished with errors");
        return ok;
    }

    public static async Task<bool> SetToggleAsync(Tweak t, bool on, Action<string> log, CancellationToken ct = default) =>
        on ? await ApplyAsync(t, log, ct) : await UndoAsync(t, log, ct);

    public static async Task<bool> ApplyChoiceAsync(Tweak t, Choice c, Action<string> log, CancellationToken ct = default)
    {
        log($"▶ {t.Name}: {c.Name}");
        var ok = WriteRegistry(c.Registry.Select(r => (r, r.Value)), log);
        foreach (var s in c.Services.Where(s => s.Start is not null))
            Services.Set(s.Name, s.Start!, log);
        if (Catalog.Script($"{t.Id}.{c.Slug}.ps1") is { } script)
            ok &= await RunScript(script, log, ct);
        log(ok ? "  done" : "  finished with errors");
        return ok;
    }

    private static bool WriteRegistry(IEnumerable<(RegEntry Entry, string? Value)> values, Action<string> log)
    {
        var ok = true;
        foreach (var (r, value) in values)
        {
            try
            {
                Reg.Write(r.Path, r.Name, r.Type, value);
                log($"  {r.Path}\\{(r.Name.Length == 0 ? "(Default)" : r.Name)} = {value ?? "(removed)"}");
            }
            catch (UnauthorizedAccessException)
            {
                ok = false;
                log($"  {r.Path}\\{r.Name}: access denied (needs administrator rights)");
            }
            catch (Exception ex)
            {
                ok = false;
                log($"  {r.Path}\\{r.Name}: {ex.Message}");
            }
        }

        return ok;
    }

    private static async Task<bool> RunScript(string script, Action<string> log, CancellationToken ct)
    {
        try
        {
            var code = await Shell.RunScriptAsync(script, l => log("  " + l), ct);
            if (code != 0) log($"  script exited with code {code}");
            return code == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log($"  couldn't run the script: {ex.Message}");
            return false;
        }
    }

    // ---------- Undo records ----------

    public static UndoRecord Snapshot(Tweak t)
    {
        var rec = new UndoRecord { Id = t.Id, AppliedAt = DateTime.Now };
        foreach (var r in t.Registry)
        {
            if (rec.Values.Any(v => v.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase) && v.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var s = Reg.Read(r.Path, r.Name);
            rec.Values.Add(new SavedValue { Path = r.Path, Name = r.Name, Exists = s.Exists, Kind = s.Kind, Data = s.Data });
        }

        foreach (var sv in t.Services)
            if (Services.StartType(sv.Name) is { } start and not "Boot")
                rec.Services.Add(new SavedService { Name = sv.Name, Start = start });
        return rec;
    }

    private static string UndoFile(string id) => Path.Combine(UndoFolder, id + ".json");

    public static UndoRecord? LoadUndo(string id)
    {
        try
        {
            var f = UndoFile(id);
            return File.Exists(f) ? JsonSerializer.Deserialize<UndoRecord>(File.ReadAllText(f), Catalog.Json) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveUndo(UndoRecord rec)
    {
        Directory.CreateDirectory(UndoFolder);
        File.WriteAllText(UndoFile(rec.Id), JsonSerializer.Serialize(rec, Catalog.Json));
    }

    private static void DeleteUndo(string id)
    {
        try { File.Delete(UndoFile(id)); } catch { }
    }

    /// <summary>Restart hints for a batch: explorer restarts now; sign-out/reboot are reported.</summary>
    public static string? FinishBatch(IEnumerable<Tweak> done, Action<string> log)
    {
        var hints = done.Select(t => t.Restart).Where(r => r is not null).Distinct().ToList();
        if (hints.Contains("refresh") || hints.Contains("explorer")) Shell.BroadcastSettingsChange();
        if (hints.Contains("explorer")) Shell.RestartExplorer(log);
        if (hints.Contains("reboot")) return "Restart the PC to finish some of these changes.";
        if (hints.Contains("signout")) return "Sign out and back in to finish some of these changes.";
        return null;
    }
}
