using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace pxdWindowsTweaks.Core;

/// <summary>One registry value a tweak sets. <c>On</c>/<c>Off</c> null means "the value is deleted".</summary>
public sealed class RegEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "DWord";
    public string? On { get; set; }
    public string? Off { get; set; }
    /// <summary>Used by choices: the value this choice sets (null deletes it).</summary>
    public string? Value { get; set; }
}

/// <summary>A service start type a tweak sets: Automatic, AutomaticDelayed, Manual or Disabled.</summary>
public sealed class SvcEntry
{
    public string Name { get; set; } = "";
    public string? On { get; set; }
    public string? Off { get; set; }
    /// <summary>Used by choices.</summary>
    public string? Start { get; set; }
}

public sealed class Choice
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Caution { get; set; }
    public List<RegEntry> Registry { get; set; } = [];
    public List<SvcEntry> Services { get; set; } = [];

    /// <summary>File-name form of the choice name ("Security only" → "security-only").</summary>
    public string Slug => string.Concat(Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
}

public sealed class Tweak
{
    public string Id { get; set; } = "";
    /// <summary>essential, advanced, preference or updates.</summary>
    public string Group { get; set; } = "essential";
    /// <summary>tweak (on/undo), action (one-shot, may have an undo script) or choice. Preferences are toggles.</summary>
    public string Kind { get; set; } = "tweak";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Caution { get; set; }
    /// <summary>What has to happen before the change shows: explorer, refresh, signout or reboot.</summary>
    public string? Restart { get; set; }
    /// <summary>Probe name for script-only tweaks whose state can still be read (see <see cref="Engine"/>).</summary>
    public string? Probe { get; set; }
    /// <summary>Windows' own default: true/false for toggles, the choice name for choices.</summary>
    public JsonElement? Default { get; set; }
    public List<RegEntry> Registry { get; set; } = [];
    public List<SvcEntry> Services { get; set; } = [];
    public List<Choice> Choices { get; set; } = [];

    [JsonIgnore] public bool IsToggle => Group == "preference" && Kind != "choice";
    [JsonIgnore] public bool IsChoice => Kind == "choice";
    [JsonIgnore] public bool IsAction => Kind == "action";
    [JsonIgnore] public string? OnScript => Catalog.Script($"{Id}.on.ps1");
    [JsonIgnore] public string? OffScript => Catalog.Script($"{Id}.off.ps1");
    [JsonIgnore] public bool CanUndo => !IsAction || OffScript is not null || Registry.Count > 0 || Services.Count > 0;

    public string? DefaultChoice => Default is { ValueKind: JsonValueKind.String } d ? d.GetString() : null;
    public bool DefaultOn => Default is { ValueKind: JsonValueKind.True };
}

public sealed class TweakCatalog
{
    public int Schema { get; set; }
    public Dictionary<string, List<string>> Presets { get; set; } = [];
    public List<Tweak> Tweaks { get; set; } = [];
}

public sealed class AppItem
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Link { get; set; } = "";
    public bool Foss { get; set; }
}

public sealed class AppCatalog
{
    public int Schema { get; set; }
    public List<AppItem> Apps { get; set; } = [];
}

/// <summary>The built-in lists, embedded in the exe.</summary>
public static class Catalog
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<TweakCatalog> _tweaks = new(() => Load<TweakCatalog>("tweaks.json"));
    private static readonly Lazy<AppCatalog> _apps = new(() => Load<AppCatalog>("apps.json"));
    private static readonly Dictionary<string, string?> _scripts = new(StringComparer.OrdinalIgnoreCase);

    public static TweakCatalog Tweaks => _tweaks.Value;
    public static List<AppItem> Apps => _apps.Value.Apps;

    public static Tweak? Find(string id) => Tweaks.Tweaks.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static T Load<T>(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException($"{name} is missing from the build");
        return JsonSerializer.Deserialize<T>(s, Json) ?? throw new InvalidOperationException($"{name} is empty");
    }

    /// <summary>An embedded script by file name (Assets\scripts\...), or null.</summary>
    public static string? Script(string file)
    {
        lock (_scripts)
        {
            if (_scripts.TryGetValue(file, out var cached)) return cached;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("scripts." + file);
            string? text = null;
            if (s is not null)
            {
                using var r = new StreamReader(s);
                text = r.ReadToEnd();
            }

            _scripts[file] = text;
            return text;
        }
    }

    public static IEnumerable<string> ScriptNames() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("scripts.")).Select(n => n["scripts.".Length..]);
}
