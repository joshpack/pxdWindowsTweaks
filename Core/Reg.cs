using System.Globalization;
using Microsoft.Win32;

namespace pxdWindowsTweaks.Core;

/// <summary>What a registry value holds right now (or that it's missing).</summary>
public sealed record RegState(bool Exists, string? Kind, string? Data)
{
    public static readonly RegState Missing = new(false, null, null);
}

/// <summary>Registry reads and writes with paths like "HKLM\SOFTWARE\...", always in the 64-bit view.</summary>
public static class Reg
{
    /// <summary>Tests point HKCU/HKLM at a scratch key so the real registry is never touched.</summary>
    public static string? Redirect { get; set; }

    private static (RegistryKey Root, string Sub) Split(string path)
    {
        var p = path.Replace('/', '\\').TrimEnd('\\');
        if (Redirect is { } r)
        {
            // HKLM\SOFTWARE\X → HKCU\<redirect>\HKLM\SOFTWARE\X
            var flat = p.Replace(":", "");
            return (RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64), r + "\\" + flat);
        }

        var i = p.IndexOf('\\');
        var hive = (i < 0 ? p : p[..i]).TrimEnd(':').ToUpperInvariant();
        var sub = i < 0 ? "" : p[(i + 1)..];
        var h = hive switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            _ => throw new ArgumentException($"Unknown registry hive in {path}"),
        };
        return (RegistryKey.OpenBaseKey(h, RegistryView.Registry64), sub);
    }

    public static bool IsValidPath(string path)
    {
        try
        {
            Split(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static RegState Read(string path, string name)
    {
        try
        {
            var (root, sub) = Split(path);
            using (root)
            using (var k = root.OpenSubKey(sub, false))
            {
                if (k is null) return RegState.Missing;
                var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v is null) return RegState.Missing;
                var kind = k.GetValueKind(name);
                return new RegState(true, kind.ToString(), Format(v, kind));
            }
        }
        catch
        {
            return RegState.Missing;
        }
    }

    private static string Format(object v, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToHexString((byte[])v),
        RegistryValueKind.MultiString => string.Join("\n", (string[])v),
        RegistryValueKind.DWord => unchecked((uint)(int)v).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => unchecked((ulong)(long)v).ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>True when the value is in the wanted state (null = absent). Numbers compare as numbers, text ignoring case.</summary>
    public static bool Matches(RegState s, string? wanted, string type)
    {
        if (wanted is null) return !s.Exists;
        if (!s.Exists) return false;
        if (IsNumber(type) && ulong.TryParse(s.Data, out var a) && ulong.TryParse(wanted, out var b)) return a == b;
        return string.Equals(s.Data, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumber(string type) => type.Equals("DWord", StringComparison.OrdinalIgnoreCase) || type.Equals("QWord", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sets the value (creating the key), or deletes it when <paramref name="data"/> is null.</summary>
    public static void Write(string path, string name, string type, string? data)
    {
        var (root, sub) = Split(path);
        using (root)
        {
            if (data is null)
            {
                using var k = root.OpenSubKey(sub, true);
                k?.DeleteValue(name, false);
                return;
            }

            using var key = root.CreateSubKey(sub, true) ?? throw new IOException($"Couldn't open {path}");
            var kind = Enum.TryParse<RegistryValueKind>(type, true, out var parsed) ? parsed : RegistryValueKind.String;
            object value = kind switch
            {
                RegistryValueKind.DWord => unchecked((int)uint.Parse(data, CultureInfo.InvariantCulture)),
                RegistryValueKind.QWord => unchecked((long)ulong.Parse(data, CultureInfo.InvariantCulture)),
                RegistryValueKind.Binary => Convert.FromHexString(data),
                RegistryValueKind.MultiString => data.Split('\n'),
                _ => data,
            };
            key.SetValue(name, value, kind);
        }
    }

    /// <summary>Restores a value exactly as it was recorded.</summary>
    public static void Restore(string path, string name, RegState s)
    {
        if (!s.Exists)
            Write(path, name, "String", null);
        else
            Write(path, name, s.Kind ?? "String", s.Data ?? "");
    }

    /// <summary>Deletes a whole key tree (test clean-up).</summary>
    public static void DeleteTree(string path)
    {
        var (root, sub) = Split(path);
        using (root)
            root.DeleteSubKeyTree(sub, false);
    }
}
