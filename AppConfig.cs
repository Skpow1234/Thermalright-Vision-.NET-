using System.Text.Json;

namespace ThermalrightVision;

public sealed class AppConfig
{
    public int Fps { get; set; } = 15;
    public int JpegQuality { get; set; } = 78;

    /// <summary>auto | stats | media</summary>
    public string Mode { get; set; } = "auto";

    public string Gif { get; set; } = "";
    public string Media { get; set; } = "";

    public int Rotate { get; set; } = 0;
    public bool FlipHorizontal { get; set; } = false;
    public bool FlipVertical { get; set; } = false;
    public string Fit { get; set; } = "contain";
    public double Zoom { get; set; } = 1.0;

    public bool ShowClock { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;

    public bool StartHidden { get; set; } = false;
    public bool RunAtLogin { get; set; } = false;

    public int? Width { get; set; }
    public int? Height { get; set; }

    public bool UseCoverFit =>
        string.Equals(Fit, "cover", StringComparison.OrdinalIgnoreCase);

    public static string ConfigPath(string baseDir) => Path.Combine(baseDir, "config.json");

    public static AppConfig Load(string baseDir)
    {
        string path = ConfigPath(baseDir);
        if (!File.Exists(path)) return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new AppConfig();
    }

    public string? ResolveMedia(string baseDir)
    {
        foreach (string candidate in new[] { Gif, Media })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Path.IsPathRooted(candidate) ? candidate : Path.Combine(baseDir, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public bool UseMedia(string? mediaPath)
    {
        string mode = (Mode ?? "auto").Trim().ToLowerInvariant();
        return mode switch
        {
            "stats" => false,
            "media" => mediaPath is not null,
            _ => mediaPath is not null
        };
    }
}
