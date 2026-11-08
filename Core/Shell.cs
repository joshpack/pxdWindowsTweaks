using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PaxWindowsTweaks.Core;

/// <summary>Runs PowerShell scripts and console tools, streaming their output line by line.</summary>
public static class Shell
{
    public static string PowerShellExe => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>Tests: when set, scripts are not run; the delegate gets the script text and returns an exit code.</summary>
    public static Func<string, int>? Fake { get; set; }

    private const string Prelude =
        "$ProgressPreference = 'SilentlyContinue'\r\n" +
        "$ErrorActionPreference = 'Continue'\r\n" +
        "try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }\r\n" +
        "trap { [Console]::Error.WriteLine(\"ERROR: \" + $_.Exception.Message); exit 1 }\r\n";

    /// <summary>Runs a script in Windows PowerShell 5.1. Returns the exit code (0 = success).</summary>
    public static async Task<int> RunScriptAsync(string script, Action<string> log, CancellationToken ct = default)
    {
        if (Fake is { } fake) return fake(script);
        var dir = Path.Combine(Path.GetTempPath(), "PaxWindowsTweaks");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"run-{Guid.NewGuid():N}.ps1");
        // UTF-8 with BOM: Windows PowerShell reads BOM-less files as the ANSI code page.
        await File.WriteAllTextAsync(file, Prelude + script + "\r\nexit 0\r\n", new UTF8Encoding(true), ct);
        try
        {
            return await RunAsync(PowerShellExe, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"", log, ct, Encoding.UTF8);
        }
        finally
        {
            try { File.Delete(file); } catch { /* still open by a child, gone with Temp */ }
        }
    }

    /// <summary>Runs a script and returns everything it printed (for read-only queries such as Get-AppxPackage).</summary>
    public static async Task<string> QueryAsync(string script, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var code = await RunScriptAsync(script, l => sb.AppendLine(l), ct);
        if (code != 0) throw new InvalidOperationException(sb.ToString().Trim());
        return sb.ToString();
    }

    public static async Task<int> RunAsync(string exe, string args, Action<string> log, CancellationToken ct = default, Encoding? encoding = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = encoding ?? Encoding.UTF8,
            StandardErrorEncoding = encoding ?? Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void Line(string? s)
        {
            if (s is null) return;
            s = Clean(s);
            if (s.Length > 0) log(s);
        }

        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);
        p.Start();
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw;
        }

        return p.ExitCode;
    }

    /// <summary>Drops progress spinners and bars (winget, DISM) and control characters from a line of output.</summary>
    public static string Clean(string s)
    {
        var last = s.LastIndexOf('\r');
        if (last >= 0 && last < s.Length - 1) s = s[(last + 1)..];
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\b' || (char.IsControl(c) && c != '\t')) continue;
            sb.Append(c);
        }

        var t = sb.ToString().TrimEnd();
        var trimmed = t.Trim();
        if (trimmed.Length <= 1 && "-\\|/".Contains(trimmed)) return "";
        // Progress bars made of block characters ("██████▒▒▒  1.2 MB / 3.4 MB")
        if (trimmed.Count(ch => ch is '█' or '▒' or '░' or '▓' or '=') > 8) return "";
        if (trimmed.StartsWith('[') && trimmed.Contains('%') && trimmed.Contains("==")) return "";
        return t;
    }

    /// <summary>Starts a Windows tool (Control Panel applet, .msc, .cpl) the normal way.</summary>
    public static void Open(string file, string args = "")
    {
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true })?.Dispose();
    }

    // ---------- Making changes show ----------

    /// <summary>Restarts Explorer so taskbar and Explorer settings apply. Windows restarts the shell by itself.</summary>
    public static void RestartExplorer(Action<string> log)
    {
        if (Fake is not null) { log("Explorer restart (skipped in the test)"); return; }
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
            p.Dispose();
        }

        Thread.Sleep(2500);
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            // Started unelevated through the shell so the taskbar doesn't run as administrator.
            try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose(); } catch { }
        }

        log("Explorer restarted.");
    }

    /// <summary>Tells running programs that settings changed (theme, policies, environment).</summary>
    public static void BroadcastSettingsChange()
    {
        if (Fake is not null) return;
        foreach (var area in new[] { "ImmersiveColorSet", "Policy", "Environment", "WindowsThemeElement" })
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, area, 0x0002, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
