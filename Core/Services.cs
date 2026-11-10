using System.Diagnostics;

namespace pxdWindowsTweaks.Core;

/// <summary>Service start types, read from the registry and changed through sc.exe so the change is live.</summary>
public static class Services
{
    /// <summary>Tests: start types are kept in this dictionary instead of the real services.</summary>
    public static Dictionary<string, string>? Fake { get; set; }

    public static bool Exists(string name) =>
        Fake?.ContainsKey(name) ?? Reg.Read($@"HKLM\SYSTEM\CurrentControlSet\Services\{name}", "Start").Exists;

    /// <summary>Automatic, AutomaticDelayed, Manual, Disabled, or null when there's no such service.</summary>
    public static string? StartType(string name)
    {
        if (Fake is { } f) return f.TryGetValue(name, out var v) ? v : null;
        var key = $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}";
        var start = Reg.Read(key, "Start");
        if (!start.Exists) return null;
        var delayed = Reg.Read(key, "DelayedAutostart");
        return start.Data switch
        {
            "0" or "1" => "Boot",
            "2" => delayed.Data == "1" ? "AutomaticDelayed" : "Automatic",
            "3" => "Manual",
            "4" => "Disabled",
            _ => start.Data,
        };
    }

    public static bool Matches(string? actual, string? wanted) =>
        wanted is null || actual is null || string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase)
        || (wanted.Equals("Automatic", StringComparison.OrdinalIgnoreCase) && actual.Equals("AutomaticDelayed", StringComparison.OrdinalIgnoreCase));

    /// <summary>Changes the start type. Missing services are skipped (returns false).</summary>
    public static bool Set(string name, string start, Action<string> log)
    {
        if (Fake is { } f)
        {
            if (!f.ContainsKey(name)) return false;
            f[name] = start;
            return true;
        }

        if (!Exists(name))
        {
            log($"  service {name}: not on this PC, skipped");
            return false;
        }

        var sc = start.ToLowerInvariant() switch
        {
            "automatic" => "auto",
            "automaticdelayed" => "delayed-auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => throw new ArgumentException($"Unknown start type {start}"),
        };
        var psi = new ProcessStartInfo("sc.exe", $"config \"{name}\" start= {sc}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        if (p.ExitCode == 0)
        {
            log($"  service {name}: {start}");
            return true;
        }

        // Some protected services refuse sc.exe but accept the registry value (takes effect after a restart).
        try
        {
            var key = $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}";
            var code = sc switch { "auto" or "delayed-auto" => "2", "demand" => "3", _ => "4" };
            Reg.Write(key, "Start", "DWord", code);
            if (sc is "auto" or "delayed-auto") Reg.Write(key, "DelayedAutostart", "DWord", sc == "delayed-auto" ? "1" : "0");
            log($"  service {name}: {start} (set in the registry, takes effect after a restart)");
            return true;
        }
        catch (Exception ex)
        {
            log($"  service {name}: couldn't change ({output.Trim()} {ex.Message})");
            return false;
        }
    }
}
