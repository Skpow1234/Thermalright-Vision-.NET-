using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace ThermalrightVision;

public sealed class AppConfig
{
    public int Fps { get; set; } = 15;
    /// <summary>FPS cuando el fotograma de media no cambia (idle). 0 = no reenviar hasta el siguiente frame.</summary>
    public int FpsIdle { get; set; } = 2;
    public int JpegQuality { get; set; } = 78;

    public string Mode { get; set; } = "auto";
    public string Gif { get; set; } = "";
    public string Media { get; set; } = "";

    public int Rotate { get; set; } = 0;
    public bool FlipHorizontal { get; set; } = false;
    public bool FlipVertical { get; set; } = false;
    public string Fit { get; set; } = "contain";
    public double Zoom { get; set; } = 1.0;
    public string LetterboxColor { get; set; } = "#000000";

    public bool ShowClock { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;

    public bool StartHidden { get; set; } = false;
    public bool RunAtLogin { get; set; } = false;
    public bool WatchConfig { get; set; } = true;

    public int? Width { get; set; }
    public int? Height { get; set; }

    public bool UseCoverFit =>
        string.Equals(Fit, "cover", StringComparison.OrdinalIgnoreCase);

    public Color ParseLetterboxColor()
    {
        string raw = (LetterboxColor ?? "#000000").Trim();
        if (raw.StartsWith('#') && raw.Length is 7 or 9)
        {
            int r = int.Parse(raw.AsSpan(1, 2), NumberStyles.HexNumber);
            int g = int.Parse(raw.AsSpan(3, 2), NumberStyles.HexNumber);
            int b = int.Parse(raw.AsSpan(5, 2), NumberStyles.HexNumber);
            return Color.FromArgb(r, g, b);
        }
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], out int rr) &&
            int.TryParse(parts[1], out int gg) &&
            int.TryParse(parts[2], out int bb))
            return Color.FromArgb(ClampByte(rr), ClampByte(gg), ClampByte(bb));
        return Color.Black;
    }

    private static int ClampByte(int v) => Math.Clamp(v, 0, 255);

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
