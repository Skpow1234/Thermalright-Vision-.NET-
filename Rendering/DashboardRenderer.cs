using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ThermalrightVision.Sensors;

namespace ThermalrightVision.Rendering;

public sealed class DashboardRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly bool _mediaMode;
    private readonly Image? _media;
    private readonly FrameDimension? _dimension;
    private readonly int _frameCount;
    private readonly int[] _durations;
    private readonly int _loopMs;
    private readonly int _rotate;
    private readonly bool _flipH;
    private readonly bool _flipV;
    private readonly bool _coverFit;
    private readonly double _zoom;
    private readonly bool _showClock;
    private readonly bool _showCpu;
    private readonly bool _showGpu;
    private readonly bool _showRam;
    private readonly Color _letterbox;
    private readonly Font _clockFont;
    private readonly Font _titleFont;
    private readonly Font _valueFont;
    private readonly Font _subFont;
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _encoder;
    private readonly Bitmap _canvas;
    private readonly Graphics _graphics;

    // Recycle one JPEG buffer stream to cut alloc churn.
    private readonly MemoryStream _jpegStream = new(128 * 1024);

    public bool MediaMode => _mediaMode;

    public int GetMediaFrameIndex(TimeSpan elapsed) => FrameIndex(elapsed);

    public int GetMediaFrameDurationMs(int index) =>
        index >= 0 && index < _durations.Length ? _durations[index] : 80;

    public DashboardRenderer(int width, int height, string? mediaPath, AppConfig config)
    {
        _width = width;
        _height = height;
        _rotate = ((config.Rotate % 360) + 360) % 360;
        _flipH = config.FlipHorizontal;
        _flipV = config.FlipVertical;
        _coverFit = config.UseCoverFit;
        _zoom = Math.Clamp(config.Zoom <= 0 ? 1.0 : config.Zoom, 0.1, 5.0);
        _showClock = config.ShowClock;
        _showCpu = config.ShowCpu;
        _showGpu = config.ShowGpu;
        _showRam = config.ShowRam;
        _letterbox = config.ParseLetterboxColor();
        int quality = Math.Clamp(config.JpegQuality, 40, 95);

        _clockFont = new Font("Segoe UI Semibold", 30, FontStyle.Bold, GraphicsUnit.Pixel);
        _titleFont = new Font("Segoe UI Semibold", 24, FontStyle.Bold, GraphicsUnit.Pixel);
        _valueFont = new Font("Segoe UI Semibold", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        _subFont = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        _encoder = new EncoderParameters(1);
        _encoder.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        _canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        _graphics = Graphics.FromImage(_canvas);
        _graphics.CompositingMode = CompositingMode.SourceCopy;
        _graphics.CompositingQuality = CompositingQuality.HighSpeed;
        _graphics.InterpolationMode = InterpolationMode.Low;
        _graphics.SmoothingMode = SmoothingMode.HighSpeed;
        _graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;

        if (!string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath))
        {
            _media = Image.FromFile(mediaPath);
            _dimension = new FrameDimension(_media.FrameDimensionsList[0]);
            _frameCount = Math.Max(1, _media.GetFrameCount(_dimension));
            _durations = new int[_frameCount];
            for (int i = 0; i < _frameCount; i++)
            {
                int duration = 80;
                try
                {
                    _media.SelectActiveFrame(_dimension, i);
                    var item = _media.GetPropertyItem(0x5100);
                    if (item.Value is { Length: >= 4 })
                        duration = Math.Max(20, BitConverter.ToInt32(item.Value, i * 4) * 10);
                }
                catch { }
                _durations[i] = duration;
            }
            _loopMs = Math.Max(1, _durations.Sum());
            _mediaMode = true;
            _graphics.CompositingMode = CompositingMode.SourceCopy;
            _graphics.InterpolationMode = InterpolationMode.Bilinear;
        }
        else
        {
            _mediaMode = false;
            _frameCount = 0;
            _durations = Array.Empty<int>();
            _loopMs = 1;
            _graphics.CompositingMode = CompositingMode.SourceOver;
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            _graphics.InterpolationMode = InterpolationMode.Bilinear;
        }
    }

    public ReadOnlyMemory<byte> RenderJpeg(Stats stats, TimeSpan elapsed, string clock)
    {
        var g = _graphics;
        g.ResetTransform();

        if (_mediaMode && _media is not null && _dimension is not null)
        {
            int index = FrameIndex(elapsed);
            _media.SelectActiveFrame(_dimension, index);
            DrawMediaCover(g, _media);
        }
        else
        {
            DrawStatsBackground(g, elapsed);
            DrawCards(g, stats, clock);
        }

        _jpegStream.SetLength(0);
        _jpegStream.Position = 0;
        _canvas.Save(_jpegStream, _jpegCodec, _encoder);
        // Reuse the MemoryStream buffer; caller must finish using it before the next RenderJpeg.
        return new ReadOnlyMemory<byte>(_jpegStream.GetBuffer(), 0, (int)_jpegStream.Length);
    }

    public void Dispose()
    {
        _graphics.Dispose();
        _canvas.Dispose();
        _media?.Dispose();
        _clockFont.Dispose();
        _titleFont.Dispose();
        _valueFont.Dispose();
        _subFont.Dispose();
        _encoder.Dispose();
        _jpegStream.Dispose();
    }

    private int FrameIndex(TimeSpan elapsed)
    {
        if (_frameCount <= 1) return 0;
        int cursor = (int)(elapsed.TotalMilliseconds % _loopMs);
        int acc = 0;
        for (int i = 0; i < _frameCount; i++)
        {
            acc += _durations[i];
            if (cursor < acc) return i;
        }
        return _frameCount - 1;
    }

    private void DrawMediaCover(Graphics g, Image source)
    {
        // Orientation via transform so we never keep rotated copies in RAM.
        ApplyOrientationTransform(g);

        bool swap = _rotate is 90 or 270;
        int targetW = swap ? _height : _width;
        int targetH = swap ? _width : _height;

        double baseScale = _coverFit
            ? Math.Max(targetW / (double)source.Width, targetH / (double)source.Height)
            : Math.Min(targetW / (double)source.Width, targetH / (double)source.Height);
        double scale = baseScale * _zoom;
        int w = Math.Max(1, (int)(source.Width * scale));
        int h = Math.Max(1, (int)(source.Height * scale));
        int x = (targetW - w) / 2;
        int y = (targetH - h) / 2;
        g.Clear(_letterbox);
        g.DrawImage(source, new Rectangle(x, y, w, h));
    }

    private void ApplyOrientationTransform(Graphics g)
    {
        // Build destination-space transform: flip then rotate around center.
        g.TranslateTransform(_width / 2f, _height / 2f);
        if (_rotate == 90) g.RotateTransform(90);
        else if (_rotate == 180) g.RotateTransform(180);
        else if (_rotate == 270) g.RotateTransform(270);
        if (_flipH || _flipV)
            g.ScaleTransform(_flipH ? -1 : 1, _flipV ? -1 : 1);

        bool swap = _rotate is 90 or 270;
        float halfW = (swap ? _height : _width) / 2f;
        float halfH = (swap ? _width : _height) / 2f;
        g.TranslateTransform(-halfW, -halfH);
    }

    private void DrawStatsBackground(Graphics g, TimeSpan elapsed)
    {
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, _width, _height),
            Color.FromArgb(6, 10, 20),
            Color.FromArgb(18, 24, 48),
            0f);
        g.FillRectangle(brush, 0, 0, _width, _height);
        double t = elapsed.TotalSeconds;
        DrawBlob(g, _width * 0.22 + Math.Cos(t * 0.28) * _width * 0.06, _height * 0.5 + Math.Sin(t * 0.25) * _height * 0.18, _height * 1.1, Color.FromArgb(130, 36, 92, 190));
        DrawBlob(g, _width * 0.5 + Math.Cos(t * 0.33 + 1) * _width * 0.05, _height * 0.45 + Math.Sin(t * 0.2) * _height * 0.16, _height * 0.95, Color.FromArgb(120, 150, 54, 170));
        DrawBlob(g, _width * 0.78 + Math.Cos(t * 0.21 + 2) * _width * 0.05, _height * 0.55 + Math.Sin(t * 0.27) * _height * 0.14, _height * 0.85, Color.FromArgb(110, 20, 150, 160));
    }

    private void DrawCards(Graphics g, Stats stats, string clock)
    {
        int marginX = (int)(_width * 0.05);
        const int gap = 24;
        int cardH = (int)(_height * 0.62);
        int top = (_height - cardH) / 2 + 8;

        if (_showClock)
        {
            using var clockBrush = new SolidBrush(Color.FromArgb(230, 235, 240, 248));
            g.DrawString(clock, _clockFont, clockBrush, marginX, 24);
        }

        var cards = new List<(string Title, string Value, string Sub, Color Accent)>();
        if (_showCpu)
        {
            string cpuSub = stats.CpuMhz is null ? "carga" : $"{stats.CpuMhz.Value / 1000:0.00} GHz";
            if (stats.CpuTemp is not null)
                cpuSub = $"{stats.CpuTemp:0}°   ·   {cpuSub}";
            cards.Add(("CPU", Text(stats.CpuPercent, "%"), cpuSub, Color.FromArgb(126, 196, 255)));
        }
        if (_showGpu)
            cards.Add(("GPU", Text(stats.GpuTemp, "°"), $"hot {Text(stats.GpuHotspot, "°")}   ·   {Text(stats.GpuPercent, "%")}", Color.FromArgb(255, 176, 92)));
        if (_showRam)
            cards.Add(("RAM", Text(stats.RamPercent, "%"), "en uso", Color.FromArgb(126, 230, 190)));
        if (cards.Count == 0) return;

        int cardW = (_width - marginX * 2 - gap * Math.Max(0, cards.Count - 1)) / cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            DrawCard(g, new Rectangle(marginX + i * (cardW + gap), top, cardW, cardH), card.Title, card.Value, card.Sub, card.Accent);
        }
    }

    private void DrawCard(Graphics g, Rectangle box, string title, string value, string subtitle, Color accent)
    {
        using var fill = new SolidBrush(Color.FromArgb(170, 8, 12, 22));
        using var path = RoundRect(box, 28);
        g.FillPath(fill, path);
        using var titleBrush = new SolidBrush(accent);
        using var valueBrush = new SolidBrush(Color.FromArgb(245, 248, 252));
        using var subBrush = new SolidBrush(Color.FromArgb(230, 190, 198, 210));
        g.DrawString(title, _titleFont, titleBrush, box.X + 28, box.Y + 20);
        g.DrawString(value, _valueFont, valueBrush, box.X + 22, box.Y + (int)(box.Height * 0.28));
        g.DrawString(subtitle, _subFont, subBrush, box.X + 28, box.Y + box.Height - 56);
    }

    private static void DrawBlob(Graphics g, double cx, double cy, double diameter, Color color)
    {
        int d = Math.Max(8, (int)diameter);
        var rect = new Rectangle((int)(cx - d / 2.0), (int)(cy - d / 2.0), d, d);
        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = color,
            SurroundColors = new[] { Color.FromArgb(0, color) }
        };
        g.FillEllipse(brush, rect);
    }

    private static GraphicsPath RoundRect(Rectangle box, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string Text(double? value, string suffix) =>
        value is null ? "--" : suffix == "%" ? $"{value:0}%" : $"{value:0}{suffix}";
}
