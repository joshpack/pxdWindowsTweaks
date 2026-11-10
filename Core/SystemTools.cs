using System.Text.Json;

namespace pxdWindowsTweaks.Core;

/// <summary>A built-in Store app that can be removed.</summary>
public sealed record BloatApp(string Package, string Name, bool Recommended, string Note = "");

/// <summary>A Windows optional feature group.</summary>
public sealed record FeatureItem(string Id, string Name, string Description, string[] Features, bool Capability = false);

/// <summary>A repair or one-off action on the Features &amp; fixes page.</summary>
public sealed record FixItem(string Id, string Name, string Description, string Script, string? Caution = null, bool Long = false);

/// <summary>A classic Windows control panel.</summary>
public sealed record PanelItem(string Name, string File, string Args = "");

public sealed record DnsProvider(string Name, string Description, string? V4a, string? V4b, string? V6a, string? V6b);

public sealed record AdapterDns(string Adapter, string Servers);

/// <summary>Debloat, optional features, fixes, legacy panels, DNS and power plans.</summary>
public static class SystemTools
{
    // ---------- Built-in apps ----------

    public static readonly BloatApp[] Bloat =
    [
        new("Microsoft.Copilot", "Copilot", true),
        new("Microsoft.BingSearch", "Bing Search", true),
        new("Microsoft.BingNews", "Microsoft News", true),
        new("Microsoft.BingWeather", "Weather", true),
        new("Microsoft.StartExperiencesApp", "Start experiences (Start menu ads)", true),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 Copilot (Office hub)", true),
        new("Microsoft.WindowsFeedbackHub", "Feedback Hub", true),
        new("Microsoft.GetHelp", "Get Help", true),
        new("Clipchamp.Clipchamp", "Clipchamp", true),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", true),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", true),
        new("Microsoft.Windows.DevHome", "Dev Home", true),
        new("Microsoft.Todos", "Microsoft To Do", true),
        new("Microsoft.MicrosoftStickyNotes", "Sticky Notes", true),
        new("Microsoft.WindowsSoundRecorder", "Sound Recorder", true),
        new("Microsoft.WindowsAlarms", "Clock", true),
        new("MicrosoftCorporationII.QuickAssist", "Quick Assist", true),
        new("Microsoft.WindowsCalculator", "Calculator", false),
        new("Microsoft.Getstarted", "Tips", false),
        new("Microsoft.WindowsMaps", "Maps", false),
        new("Microsoft.People", "People", false),
        new("microsoft.windowscommunicationsapps", "Mail and Calendar (old)", false),
        new("Microsoft.OutlookForWindows", "Outlook (new)", false),
        new("MSTeams", "Microsoft Teams (free)", false),
        new("Microsoft.SkypeApp", "Skype", false),
        new("Microsoft.YourPhone", "Phone Link", false),
        new("Microsoft.ZuneMusic", "Media Player", false),
        new("Microsoft.ZuneVideo", "Films & TV", false),
        new("Microsoft.GamingApp", "Xbox app", false, "Needed for Game Pass PC games"),
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", false, "Also used for screen recording"),
        new("Microsoft.Xbox.TCUI", "Xbox sign-in pieces", false),
        new("Microsoft.XboxIdentityProvider", "Xbox identity provider", false, "Needed to sign in to Xbox games"),
        new("Microsoft.MixedReality.Portal", "Mixed Reality Portal", false),
        new("Microsoft.Microsoft3DViewer", "3D Viewer", false),
        new("Microsoft.Office.OneNote", "OneNote (Store)", false),
        new("Microsoft.BingFinance", "Money", false),
        new("Microsoft.BingSports", "Sports", false),
        new("Microsoft.549981C3F5F10", "Cortana", false),
        new("Microsoft.WindowsCamera", "Camera", false),
        new("Microsoft.Windows.Photos", "Photos", false),
        new("Microsoft.Paint", "Paint", false),
        new("Microsoft.ScreenSketch", "Snipping Tool", false),
        new("Microsoft.WindowsNotepad", "Notepad", false),
        new("7EE7776C.LinkedInforWindows", "LinkedIn", false),
        new("SpotifyAB.SpotifyMusic", "Spotify", false),
        new("Disney.37853FA22FAF4", "Disney+", false),
        new("BytedancePte.Ltd.TikTok", "TikTok", false),
        new("Facebook.InstagramBeta", "Instagram", false),
        new("AmazonVideo.PrimeVideo", "Prime Video", false),
        new("king.com.CandyCrushSaga", "Candy Crush Saga", false),
        new("king.com.CandyCrushSodaSaga", "Candy Crush Soda Saga", false),
    ];

    public static HashSet<string>? FakeInstalledAppx { get; set; }

    /// <summary>Package names of the listed apps that are installed for any user (or provisioned for new users).</summary>
    public static async Task<HashSet<string>> InstalledAppxAsync(CancellationToken ct = default)
    {
        if (FakeInstalledAppx is { } f) return new HashSet<string>(f, StringComparer.OrdinalIgnoreCase);
        var names = string.Join(",", Bloat.Select(b => $"'{b.Package}'"));
        var json = await Shell.QueryAsync(
            $"$want = @({names})\n" +
            // -AllUsers needs administrator rights; without them, this user's apps.
            "$pkgs = try { Get-AppxPackage -AllUsers -ErrorAction Stop } catch { Get-AppxPackage }\n" +
            "$have = @($pkgs | Select-Object -ExpandProperty Name) + " +
            "@(try { Get-AppxProvisionedPackage -Online -ErrorAction Stop | Select-Object -ExpandProperty DisplayName } catch { })\n" +
            "ConvertTo-Json -Compress @($want | Where-Object { $have -contains $_ })", ct);
        return ParseStrings(json);
    }

    public static Task<int> RemoveAppxAsync(IEnumerable<string> packages, Action<string> log, CancellationToken ct = default)
    {
        var list = string.Join(",", packages.Select(p => $"'{p}'"));
        return Shell.RunScriptAsync(
            $"foreach ($name in @({list})) {{\n" +
            "  $found = $false\n" +
            "  Get-AppxPackage -Name $name -AllUsers -ErrorAction SilentlyContinue | ForEach-Object { $found = $true; try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop } catch { Write-Host \"  $name : $($_.Exception.Message)\" } }\n" +
            "  Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object DisplayName -eq $name | ForEach-Object { $found = $true; Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null }\n" +
            "  if ($found) { Write-Host \"Removed $name\" } else { Write-Host \"$name is not installed\" }\n" +
            "}", log, ct);
    }

    // ---------- Optional features ----------

    public static readonly FeatureItem[] Features =
    [
        new("dotnet", ".NET Framework 3.5 and 4.8 extras", "Older programs and games that need .NET 2.0/3.0/3.5.", ["NetFx3", "NetFx4-AdvSrvs"]),
        new("hyperv", "Hyper-V", "Microsoft's virtual machine platform (Pro, Enterprise and Education editions).", ["Microsoft-Hyper-V-All"]),
        new("wsl", "Windows Subsystem for Linux", "Run Linux distributions; also turns on Virtual Machine Platform.", ["Microsoft-Windows-Subsystem-Linux", "VirtualMachinePlatform"]),
        new("sandbox", "Windows Sandbox", "A throwaway desktop for testing programs safely (Pro and up).", ["Containers-DisposableClientVM"]),
        new("legacymedia", "Legacy media (Windows Media Player, DirectPlay)", "For old games and programs that need DirectPlay or the classic Media Player.", ["WindowsMediaPlayer", "MediaPlayback", "DirectPlay", "LegacyComponents"]),
        new("nfs", "NFS client", "Mount NFS shares from Linux servers and NAS boxes.", ["ServicesForNFS-ClientOnly", "ClientForNFS-Infrastructure", "NFS-Administration"]),
        new("telnet", "Telnet client", "The classic telnet command, handy for testing ports.", ["TelnetClient"]),
        new("smb1", "SMB 1.0 client", "Only for very old NAS boxes and printers. Insecure; leave off unless you need it.", ["SMB1Protocol-Client"]),
    ];

    public static Dictionary<string, string>? FakeFeatureStates { get; set; }

    /// <summary>Feature name → Enabled / Disabled / EnablePending… for every feature the list uses.</summary>
    public static async Task<Dictionary<string, string>> FeatureStatesAsync(CancellationToken ct = default)
    {
        if (FakeFeatureStates is { } f) return new Dictionary<string, string>(f, StringComparer.OrdinalIgnoreCase);
        var names = string.Join(",", Features.SelectMany(x => x.Features).Select(n => $"'{n}'"));
        var json = await Shell.QueryAsync(
            $"$want = @({names})\n" +
            "$map = @{}\n" +
            "Get-WindowsOptionalFeature -Online -ErrorAction Stop | Where-Object { $want -contains $_.FeatureName } | ForEach-Object { $map[$_.FeatureName] = [string]$_.State }\n" +
            "ConvertTo-Json -Compress $map", ct);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = json.IndexOf('{');
        if (start < 0) return result;
        using var doc = JsonDocument.Parse(json[start..]);
        foreach (var p in doc.RootElement.EnumerateObject()) result[p.Name] = p.Value.GetString() ?? "";
        return result;
    }

    public static Task<int> SetFeaturesAsync(IEnumerable<string> features, bool enable, Action<string> log, CancellationToken ct = default)
    {
        var list = string.Join(",", features.Select(n => $"'{n}'"));
        var verb = enable ? "Enable-WindowsOptionalFeature -All" : "Disable-WindowsOptionalFeature";
        return Shell.RunScriptAsync(
            $"$restart = $false\nforeach ($f in @({list})) {{\n" +
            "  if (-not (Get-WindowsOptionalFeature -Online -FeatureName $f -ErrorAction SilentlyContinue)) { Write-Host \"$f isn't available on this edition of Windows\"; continue }\n" +
            $"  Write-Host \"{(enable ? "Turning on" : "Turning off")} $f...\"\n" +
            $"  $r = {verb} -Online -FeatureName $f -NoRestart -ErrorAction Continue\n" +
            "  if ($r.RestartNeeded) { $restart = $true }\n" +
            "}\nif ($restart) { Write-Host 'Restart the PC to finish.' }", log, ct);
    }

    // ---------- Fixes ----------

    public static readonly FixItem[] Fixes =
    [
        new("restore-point", "Create a restore point", "A System Restore point you can roll back to from Windows' recovery options.", "restore-point.on.ps1"),
        new("system-repair", "Scan and repair Windows", "chkdsk online scan, SFC, DISM RestoreHealth and SFC again. Takes 15 to 45 minutes.", "fix-system-repair.ps1", Long: true),
        new("windows-update", "Reset Windows Update", "Stops the update services, sets aside the download cache and catroot2, re-registers components and resets the proxy and Winsock. Fixes most stuck updates.", "fix-windows-update.ps1", "Update history in Settings starts empty.", Long: true),
        new("network", "Reset networking", "Resets Winsock and the TCP/IP stack and flushes DNS. Restart afterwards.", "fix-network.ps1"),
        new("winget", "Reinstall winget", "Re-registers App Installer, or downloads the latest one, and resets its sources.", "fix-winget.ps1"),
        new("ntp", "Sync the clock with pool.ntp.org", "Uses the public NTP pool instead of time.windows.com, then syncs now.", "fix-ntp.ps1"),
        new("registry-backup", "Nightly registry backup", "Turns RegBack back on and schedules a backup every night at 00:30.", "fix-registry-backup.ps1"),
        new("f8-on", "F8 boot recovery menu: on", "Press F8 at start-up for Safe Mode and the other recovery options.", "fix-f8-on.ps1"),
        new("f8-off", "F8 boot recovery menu: off", "Back to Windows' normal fast boot menu.", "fix-f8-off.ps1"),
        new("ssh-server", "Turn on OpenSSH Server", "Installs Windows' OpenSSH server, starts it with Windows and opens port 22 in the firewall.", "fix-ssh-server.ps1", "Anyone on your network can try to sign in."),
        new("autologon", "Sign in automatically (Autologon)", "Installs Sysinternals Autologon with winget and opens it, so you can set an account to sign in on start.", "fix-autologon.ps1", "Your password is stored on the PC (encrypted)."),
    ];

    public static readonly PanelItem[] Panels =
    [
        new("Control Panel", "control.exe"),
        new("Computer Management", "compmgmt.msc"),
        new("Network Connections", "ncpa.cpl"),
        new("Power Options", "powercfg.cpl"),
        new("Programs and Features", "appwiz.cpl"),
        new("Sound", "mmsys.cpl"),
        new("System Properties", "sysdm.cpl"),
        new("Mouse", "main.cpl"),
        new("Devices and Printers", "control.exe", "printers"),
        new("Region", "intl.cpl"),
        new("Date and Time", "timedate.cpl"),
        new("Firewall", "firewall.cpl"),
        new("Security and Maintenance", "wscui.cpl"),
        new("System Restore", "rstrui.exe"),
        new("Services", "services.msc"),
        new("Device Manager", "devmgmt.msc"),
    ];

    // ---------- DNS ----------

    public static readonly DnsProvider[] Dns =
    [
        new("Automatic (DHCP)", "Whatever your router hands out.", null, null, null, null),
        new("Cloudflare", "Fast, privacy-focused.", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
        new("Cloudflare (blocks malware)", "Cloudflare with malware blocking.", "1.1.1.2", "1.0.0.2", "2606:4700:4700::1112", "2606:4700:4700::1002"),
        new("Cloudflare (malware + adult)", "Family filter.", "1.1.1.3", "1.0.0.3", "2606:4700:4700::1113", "2606:4700:4700::1003"),
        new("Google", "Google Public DNS.", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
        new("Quad9", "Blocks known malicious domains.", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
        new("OpenDNS", "Cisco OpenDNS.", "208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"),
        new("AdGuard", "Blocks ads and trackers.", "94.140.14.14", "94.140.15.15", "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"),
        new("AdGuard Family", "Ads, trackers, malware and adult sites blocked.", "94.140.14.15", "94.140.15.16", "2a10:50c0::bad1:ff", "2a10:50c0::bad2:ff"),
    ];

    public static List<AdapterDns>? FakeDns { get; set; }

    public static async Task<List<AdapterDns>> CurrentDnsAsync(CancellationToken ct = default)
    {
        if (FakeDns is { } f) return f;
        var json = await Shell.QueryAsync(
            "$up = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up'\n" +
            "$rows = foreach ($a in $up) { $s = (Get-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ErrorAction SilentlyContinue | ForEach-Object ServerAddresses) -join ', '; [pscustomobject]@{ Adapter = $a.Name; Servers = $s } }\n" +
            "ConvertTo-Json -Compress @($rows)", ct);
        var list = new List<AdapterDns>();
        var start = json.IndexOf('[');
        if (start < 0) return list;
        using var doc = JsonDocument.Parse(json[start..]);
        foreach (var e in doc.RootElement.EnumerateArray())
            list.Add(new AdapterDns(e.GetProperty("Adapter").GetString() ?? "", e.GetProperty("Servers").GetString() ?? ""));
        return list;
    }

    public static Task<int> SetDnsAsync(DnsProvider p, Action<string> log, CancellationToken ct = default)
    {
        string body;
        if (p.V4a is null)
            body = "Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ResetServerAddresses";
        else
            body = $"Set-DnsClientServerAddress -InterfaceIndex $a.ifIndex -ServerAddresses @('{p.V4a}','{p.V4b}','{p.V6a}','{p.V6b}')";
        return Shell.RunScriptAsync(
            "foreach ($a in (Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up')) {\n" +
            $"  try {{ {body} -ErrorAction Stop; Write-Host \"$($a.Name): {p.Name}\" }} catch {{ Write-Host \"$($a.Name): $($_.Exception.Message)\" }}\n" +
            "}\nClear-DnsClientCache", log, ct);
    }

    // ---------- Power ----------

    public static async Task<string> ActivePowerPlanAsync(CancellationToken ct = default)
    {
        if (FakeDns is not null) return "Balanced";
        var sb = new System.Text.StringBuilder();
        await Shell.RunAsync("powercfg.exe", "/getactivescheme", l => sb.AppendLine(l), ct, System.Text.Encoding.Default);
        var s = sb.ToString();
        var open = s.IndexOf('(');
        var close = s.LastIndexOf(')');
        return open >= 0 && close > open ? s[(open + 1)..close] : s.Trim();
    }

    internal static HashSet<string> ParseStrings(string json)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = json.IndexOfAny(['[', '"']);
        if (start < 0) return set;
        using var doc = JsonDocument.Parse(json[start..]);
        if (doc.RootElement.ValueKind == JsonValueKind.String) set.Add(doc.RootElement.GetString()!);
        else
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.GetString() is { } s) set.Add(s);
        return set;
    }
}
