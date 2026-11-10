using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using pxdWindowsTweaks.Core;

namespace pxdWindowsTweaks;

internal static class Program
{
    public const string AppId = "pxdWindowsTweaks";

    /// <summary>Why the app runs without administrator rights, for the notice in the window (null when elevated).</summary>
    public static string? LimitedReason { get; private set; }

    /// <summary>%APPDATA%\pxdWindowsTweaks (window placement, undo records, logs), or PXDWT_DATA (tests).</summary>
    public static string DataFolder =>
        Environment.GetEnvironmentVariable("PXDWT_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pxdWindowsTweaks");

    private static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? After(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (After(args, "--write-icon") is { } icon)
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            BrandIcon.Write(icon);
            return 0;
        }

        if (After(args, "--self-test") is { } testDir)
        {
            SelfTest.Dir = Path.GetFullPath(testDir);
            SelfTest.Sample = Has(args, "--sample");
            Directory.CreateDirectory(SelfTest.Dir);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PXDWT_DATA")))
                Environment.SetEnvironmentVariable("PXDWT_DATA", Path.Combine(SelfTest.Dir, "data"));
            pxdStandard.pxdChrome.Configure(AppId, DataFolder);
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            using var done = new CancellationTokenSource();
            Dispatcher.UIThread.Post(async () =>
            {
                await SelfTest.RunAsync();
                done.Cancel();
            });
            Dispatcher.UIThread.MainLoop(done.Token);
            return SelfTest.ExitCode;
        }

        // Undo records, logs and placement kept under the app's former name move before anything reads them.
        Migration.Run();

        if (After(args, "--apply") is { } profile)
            return ApplyProfile(args, profile);

        if (!Elevation.IsElevated && !Has(args, "--no-elevate"))
        {
            // Already open? Hand over. Otherwise ask Windows for administrator rights and let that copy run.
            if (Mutex.TryOpenExisting(@"Local\" + SingleInstance.Name(AppId), out var m))
            {
                m.Dispose();
                if (SingleInstance.Send(SingleInstance.Name(AppId), args)) return 0;
            }

            if (!Has(args, "--elevated"))
            {
                var why = Elevation.RestartElevated(args);
                if (why is null) return 0;
                LimitedReason = why == "cancelled"
                    ? "Running without administrator rights because the Windows prompt was cancelled. Most tweaks need them."
                    : $"Couldn't get administrator rights ({why}). Most tweaks need them.";
            }
            else
            {
                LimitedReason = "Running without administrator rights. Most tweaks need them.";
            }
        }
        else if (!Elevation.IsElevated)
        {
            LimitedReason = "Started with --no-elevate: only your own settings (HKCU) can be changed.";
        }

        if (!SingleInstance.Claim(SingleInstance.Name(AppId), args))
            return 0;

        pxdStandard.pxdChrome.Configure(AppId, DataFolder);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>--apply profile.json: runs a saved selection without a window (for setting up new PCs).</summary>
    private static int ApplyProfile(string[] args, string file)
    {
        file = Path.GetFullPath(file);
        if (!Elevation.IsElevated && !Has(args, "--no-elevate"))
        {
            var info = Elevation.RestartInfo(args.Select(a => a.Equals(args[Array.IndexOf(args, "--apply") + 1]) ? file : a).ToArray());
            try
            {
                using var p = System.Diagnostics.Process.Start(info);
                p?.WaitForExit();
                return p?.ExitCode ?? 1;
            }
            catch
            {
                return 5;   // no administrator rights
            }
        }

        var logFile = After(args, "--log") ?? Path.Combine(DataFolder, "logs", $"apply-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFile))!);
        using var w = new StreamWriter(logFile, append: false) { AutoFlush = true };
        void Log(string s) => w.WriteLine($"{DateTime.Now:HH:mm:ss} {s}");

        try
        {
            var prof = TweakProfile.Load(file);
            Log($"pxd Windows Tweaks: applying {file}");
            var failed = prof.RunAsync(Log).GetAwaiter().GetResult();
            Log(failed == 0 ? "Finished." : $"Finished with {failed} problem(s).");
            return failed == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Log("Couldn't apply: " + ex.Message);
            return 1;
        }
    }

    /// <summary>Software rendering keeps start-up quick and memory low for a window of lists and switches.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            .LogToTrace();
}
