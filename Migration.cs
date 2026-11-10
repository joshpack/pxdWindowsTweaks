using pxdStandard;
using pxdWindowsTweaks.Core;

namespace pxdWindowsTweaks;

/// <summary>
/// One-time moves from the app's name before the pxd rename. Everything here is safe to repeat and never throws.
/// - The data folder %APPDATA%\&lt;former name&gt; (undo records, logs, window placement) becomes %APPDATA%\pxdWindowsTweaks,
///   moved whole so Undo keeps working for tweaks applied by the older version.
/// - The nightly registry backup task (System tools → Fixes) moves from the former task folder and name to
///   \pxd\pxd Registry Backup, keeping its definition and enabled state. That needs administrator rights, so it
///   happens the first time the app runs elevated; until then the old task keeps working.
/// - The Adobe hosts-file block keeps working through its scripts (adobe-block.on/off.ps1 accept the former markers).
/// The app has no autostart, file associations or other registrations to move.
/// </summary>
internal static class Migration
{
    /// <summary>The app id before the rename (from pxdMigrate, not a new literal).</summary>
    private static string FormerId => pxdMigrate.FormerName(Program.AppId);

    /// <summary>The former brand as it was written in task names and folders.</summary>
    private static string FormerBrand => pxdMigrate.FormerPrefix.ToUpperInvariant();

    /// <summary>Where Windows keeps the task definitions (readable when elevated).</summary>
    public static string TasksRoot => Path.Combine(Environment.SystemDirectory, "Tasks");

    /// <summary>Start-up: moves the data folder now (before anything reads it); the task moves in the background.</summary>
    public static void Run()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PXDWT_DATA")))
            DataFolder(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (Elevation.IsElevated)
            _ = Task.Run(() => RegistryBackupTaskAsync(TasksRoot, elevated: true));
    }

    /// <summary>Moves the former data folder under <paramref name="appDataRoot"/> to the new one. True if it moved.</summary>
    public static bool DataFolder(string appDataRoot) =>
        pxdMigrate.Folder(Path.Combine(appDataRoot, FormerId), Path.Combine(appDataRoot, Program.AppId));

    /// <summary>True when the former registry backup task is defined under <paramref name="tasksRoot"/>.</summary>
    public static bool HasFormerTask(string tasksRoot)
    {
        try
        {
            return File.Exists(Path.Combine(tasksRoot, FormerBrand, FormerBrand + " Registry Backup"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The PowerShell that moves the task: the former definition (XML, so any change the user made and its enabled
    /// state carry over) is registered as \pxd\pxd Registry Backup, then the former task and its now empty folder go.
    /// </summary>
    public static string TaskScript =>
        $$"""
        $oldPath = '\{{FormerBrand}}\'; $oldName = '{{FormerBrand}} Registry Backup'
        if (-not (Get-ScheduledTask -TaskPath $oldPath -TaskName $oldName -ErrorAction SilentlyContinue)) { Write-Host 'Nothing to move.'; return }
        if (-not (Get-ScheduledTask -TaskPath '\pxd\' -TaskName 'pxd Registry Backup' -ErrorAction SilentlyContinue)) {
            $xml = Export-ScheduledTask -TaskPath $oldPath -TaskName $oldName -ErrorAction Stop
            Register-ScheduledTask -TaskPath '\pxd\' -TaskName 'pxd Registry Backup' -Xml $xml -User 'SYSTEM' -ErrorAction Stop | Out-Null
        }
        Unregister-ScheduledTask -TaskPath $oldPath -TaskName $oldName -Confirm:$false -ErrorAction Stop
        try {
            $s = New-Object -ComObject Schedule.Service; $s.Connect()
            $f = $s.GetFolder('\{{FormerBrand}}')
            if ($f.GetTasks(1).Count -eq 0 -and $f.GetFolders(0).Count -eq 0) { $s.GetFolder('\').DeleteFolder('{{FormerBrand}}', 0) }
        } catch { }
        Write-Host 'Registry backup task moved to \pxd\pxd Registry Backup.'
        """;

    /// <summary>Moves the former registry backup task when it exists and the app is elevated. True if it moved.</summary>
    public static async Task<bool> RegistryBackupTaskAsync(string tasksRoot, bool elevated)
    {
        try
        {
            if (!elevated || !HasFormerTask(tasksRoot))
                return false;
            return await Shell.RunScriptAsync(TaskScript, _ => { }) == 0;
        }
        catch
        {
            return false;
        }
    }
}
