using System.Text.Json;

namespace PaxWindowsTweaks.Core;

/// <summary>Installs, upgrades and removes programs with Windows' own package manager.</summary>
public static class Winget
{
    private const string Agree = "--accept-source-agreements --disable-interactivity";
    private const string AgreeAll = "--accept-source-agreements --accept-package-agreements --disable-interactivity";

    /// <summary>Tests and screenshots: a made-up list of installed ids instead of asking winget.</summary>
    public static HashSet<string>? FakeInstalled { get; set; }

    public static string? ExePath()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(alias)) return alias;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = Path.Combine(dir.Trim(), "winget.exe");
            if (File.Exists(f)) return f;
        }

        return null;
    }

    public static bool Available => FakeInstalled is not null || ExePath() is not null;

    private static string Exe => ExePath() ?? throw new InvalidOperationException("winget isn't installed. Use Features & fixes → Reinstall winget, or install App Installer from the Microsoft Store.");

    /// <summary>Ids of installed packages winget knows about (from <c>winget export</c>, which is reliable to parse).</summary>
    public static async Task<HashSet<string>> InstalledAsync(CancellationToken ct = default)
    {
        if (FakeInstalled is { } fake) return new HashSet<string>(fake, StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(Path.GetTempPath(), "PaxWindowsTweaks", $"winget-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            await Shell.RunAsync(Exe, $"export -o \"{file}\" --source winget {Agree}", _ => { }, ct);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return set;
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
            foreach (var source in doc.RootElement.GetProperty("Sources").EnumerateArray())
            foreach (var pkg in source.GetProperty("Packages").EnumerateArray())
                if (pkg.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { } s)
                    set.Add(s);
            return set;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static Task<int> InstallAsync(string id, Action<string> log, CancellationToken ct = default) =>
        Shell.RunAsync(Exe, $"install --id \"{id}\" --exact --source winget --silent {AgreeAll}", log, ct);

    public static Task<int> UpgradeAsync(string id, Action<string> log, CancellationToken ct = default) =>
        Shell.RunAsync(Exe, $"upgrade --id \"{id}\" --exact --source winget --silent {AgreeAll}", log, ct);

    public static Task<int> UninstallAsync(string id, Action<string> log, CancellationToken ct = default) =>
        Shell.RunAsync(Exe, $"uninstall --id \"{id}\" --exact --silent {Agree}", log, ct);

    public static Task<int> UpgradeAllAsync(Action<string> log, CancellationToken ct = default) =>
        Shell.RunAsync(Exe, $"upgrade --all --silent --include-unknown {AgreeAll}", log, ct);

    /// <summary>winget's exit codes that still mean "fine".</summary>
    public static bool Succeeded(int code) => code is 0
        or unchecked((int)0x8A15002B)   // no applicable upgrade (already up to date)
        or unchecked((int)0x8A150061)   // already installed
        or unchecked((int)0x8A150109);  // installed, restart required

    public static string Describe(int code) => code switch
    {
        0 => "done",
        unchecked((int)0x8A15002B) => "already up to date",
        unchecked((int)0x8A150061) => "already installed",
        unchecked((int)0x8A150109) => "done, restart needed",
        unchecked((int)0x8A150014) => "not found in winget",
        unchecked((int)0x8A150010) => "no installed copy found",
        _ => $"failed (code 0x{code:X8})",
    };
}
