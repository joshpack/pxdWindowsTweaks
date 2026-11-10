using System.Text.Json;

namespace pxdWindowsTweaks.Core;

/// <summary>
/// A saved selection (Export / Import, and <c>--apply file.json</c>): tweaks to apply, preferences to set, apps to
/// install, built-in apps to remove, features to turn on and a Windows Update mode.
/// </summary>
public sealed class TweakProfile
{
    public int Schema { get; set; } = 1;
    public List<string> Tweaks { get; set; } = [];
    public Dictionary<string, bool> Preferences { get; set; } = [];
    public Dictionary<string, string> Choices { get; set; } = [];
    public List<string> Apps { get; set; } = [];
    public List<string> RemoveApps { get; set; } = [];
    public List<string> Features { get; set; } = [];

    public static TweakProfile Load(string file) =>
        JsonSerializer.Deserialize<TweakProfile>(File.ReadAllText(file), Catalog.Json) ?? new TweakProfile();

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, Catalog.Json));

    public bool IsEmpty => Tweaks.Count + Preferences.Count + Choices.Count + Apps.Count + RemoveApps.Count + Features.Count == 0;

    /// <summary>Runs everything in the profile in a sensible order (restore point first). Returns the number of failures.</summary>
    public async Task<int> RunAsync(Action<string> log, CancellationToken ct = default)
    {
        var failed = 0;
        var done = new List<Tweak>();
        var tweaks = Tweaks.Select(Catalog.Find).Where(t => t is not null).Cast<Tweak>()
            .OrderBy(t => t.Id == "restore-point" ? 0 : 1).ToList();
        foreach (var id in Tweaks.Where(id => Catalog.Find(id) is null)) log($"Unknown tweak '{id}', skipped");
        foreach (var t in tweaks)
        {
            if (!await Engine.ApplyAsync(t, log, ct)) failed++;
            done.Add(t);
        }

        foreach (var (id, on) in Preferences)
        {
            if (Catalog.Find(id) is not { IsToggle: true } t) { log($"Unknown preference '{id}', skipped"); continue; }
            if (!await Engine.SetToggleAsync(t, on, log, ct)) failed++;
            done.Add(t);
        }

        foreach (var (id, name) in Choices)
        {
            var t = Catalog.Find(id);
            var c = t?.Choices.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (t is null || c is null) { log($"Unknown setting '{id}: {name}', skipped"); continue; }
            if (!await Engine.ApplyChoiceAsync(t, c, log, ct)) failed++;
            done.Add(t);
        }

        if (RemoveApps.Count > 0)
        {
            log($"▶ Removing {RemoveApps.Count} built-in app(s)");
            if (await SystemTools.RemoveAppxAsync(RemoveApps, l => log("  " + l), ct) != 0) failed++;
        }

        var features = SystemTools.Features.Where(f => Features.Contains(f.Id, StringComparer.OrdinalIgnoreCase)).SelectMany(f => f.Features).ToList();
        if (features.Count > 0)
        {
            log($"▶ Turning on {features.Count} Windows feature(s)");
            if (await SystemTools.SetFeaturesAsync(features, true, l => log("  " + l), ct) != 0) failed++;
        }

        foreach (var id in Apps)
        {
            log($"▶ Installing {id}");
            var code = await Winget.InstallAsync(id, l => log("  " + l), ct);
            log("  " + Winget.Describe(code));
            if (!Winget.Succeeded(code)) failed++;
        }

        if (Engine.FinishBatch(done, log) is { } hint) log(hint);
        return failed;
    }
}
