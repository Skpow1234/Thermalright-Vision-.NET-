using System.Drawing;
using ThermalrightVision.Rendering;
using ThermalrightVision.Sensors;
using ThermalrightVision.Usb;

namespace ThermalrightVision;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly CancellationTokenSource _cts = new();
    private readonly StatsReader _stats = new();
    private readonly string _baseDir = AppContext.BaseDirectory;
    private readonly object _gate = new();
    private ConfigWatcher? _watcher;
    private WinUsbPanel? _panel;
    private DashboardRenderer? _renderer;
    private AppConfig _config;
    private List<string> _playlist = new();
    private int _slideIndex;
    private volatile bool _reloadRequested;
    private string _status = "Iniciando…";

    public TrayAppContext()
    {
        _config = AppConfig.Load(_baseDir);
        StartupHelper.Apply(_config.RunAtLogin, Path.Combine(_baseDir, "ThermalrightVision.exe"));
        _watcher = new ConfigWatcher(_baseDir, RequestReload, _config.WatchConfig);

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Thermalright Vision",
            Icon = SystemIcons.Application,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowStatus();
        _tray.ShowBalloonTip(2500, "Thermalright Vision", "Corriendo en segundo plano. Cierra TRCC si estaba abierto.", ToolTipIcon.Info);
        _ = Task.Run(RunAsync);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Estado", null, (_, _) => ShowStatus());
        menu.Items.Add("Recargar config", null, (_, _) => RequestReload());
        menu.Items.Add("Abrir carpeta de config", null, (_, _) => StartupHelper.OpenConfigFolder(_baseDir));
        menu.Items.Add(StartupHelper.IsEnabled() ? "No iniciar con Windows" : "Iniciar con Windows", null, (_, _) => ToggleLogin());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Exit());
        return menu;
    }

    private void RequestReload() => _reloadRequested = true;

    private void ToggleLogin()
    {
        bool next = !StartupHelper.IsEnabled();
        StartupHelper.Apply(next, Path.Combine(_baseDir, "ThermalrightVision.exe"));
        _tray.ContextMenuStrip = BuildMenu();
        _tray.ShowBalloonTip(2000, "Thermalright Vision", next ? "Se iniciará con Windows." : "Ya no inicia con Windows.", ToolTipIcon.Info);
    }

    private void ShowStatus()
    {
        string detail = _panel is null
            ? _status
            : $"{_status}\nPM={_panel.Pm} SUB={_panel.Sub}\n{_panel.Width}x{_panel.Height}";
        _tray.ShowBalloonTip(4000, "Thermalright Vision", detail, ToolTipIcon.Info);
        _tray.Text = Truncate($"Thermalright Vision — {_status}");
    }

    private async Task RunAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_panel is null)
                {
                    _panel = new WinUsbPanel();
                    (int, int)? overrideSize = _config.Width is int w && _config.Height is int h ? (w, h) : null;
                    _panel.Open(overrideSize);
                }

                RebuildRenderer(DateTime.UtcNow);
                ShowStatus();

                var started = DateTime.UtcNow;
                int lastMediaFrame = -1;
                byte[]? cachedJpeg = null;
                while (!token.IsCancellationRequested && !_reloadRequested)
                {
                    var frameStarted = DateTime.UtcNow;
                    if (TryAdvanceSlide(started, frameStarted))
                    {
                        lastMediaFrame = -1;
                        cachedJpeg = null;
                    }

                    DashboardRenderer? renderer;
                    WinUsbPanel? panel;
                    AppConfig config;
                    lock (_gate)
                    {
                        renderer = _renderer;
                        panel = _panel;
                        config = _config;
                    }
                    if (renderer is null || panel is null) break;

                    var elapsed = frameStarted - started;
                    bool encode = true;
                    int sleepMs = Math.Max(1, 1000 / Math.Max(1, config.Fps));

                    if (renderer.MediaMode)
                    {
                        int index = renderer.GetMediaFrameIndex(elapsed);
                        int frameMs = renderer.GetMediaFrameDurationMs(index);
                        if (index == lastMediaFrame)
                        {
                            // Same GIF/static frame: avoid re-encoding, but keep
                            // re-sending JPEG or the panel falls back to its logo.
                            encode = false;
                            sleepMs = config.FpsIdle <= 0
                                ? Math.Max(250, frameMs)
                                : Math.Max(16, 1000 / Math.Max(1, config.FpsIdle));
                            if (config.SlideSeconds > 0 && _playlist.Count > 1)
                                sleepMs = Math.Min(sleepMs, 1000);

                            if (cachedJpeg is not null)
                                panel.SendJpeg(cachedJpeg);
                            else
                                encode = true;
                        }
                        else
                        {
                            lastMediaFrame = index;
                            sleepMs = Math.Min(sleepMs, Math.Max(16, frameMs));
                        }
                    }

                    if (encode)
                    {
                        var jpeg = renderer.RenderJpeg(_stats.Sample(), elapsed, DateTime.Now.ToString("HH:mm"));
                        cachedJpeg = jpeg.ToArray();
                        panel.SendJpeg(cachedJpeg);
                    }

                    var spent = DateTime.UtcNow - frameStarted;
                    var delay = TimeSpan.FromMilliseconds(sleepMs) - spent;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, token);
                }

                if (_reloadRequested)
                {
                    _reloadRequested = false;
                    _config = AppConfig.Load(_baseDir);
                    StartupHelper.Apply(_config.RunAtLogin, Path.Combine(_baseDir, "ThermalrightVision.exe"));
                    _watcher?.Dispose();
                    _watcher = new ConfigWatcher(_baseDir, RequestReload, _config.WatchConfig);
                    _tray.ContextMenuStrip = BuildMenu();
                    _tray.ShowBalloonTip(2000, "Thermalright Vision", "Config recargada", ToolTipIcon.Info);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _status = "Error: " + Truncate(ex.Message, 80);
                Log("ERR " + ex);
                ShowStatus();
                DisposePanel();
                try { await Task.Delay(3000, token); } catch { break; }
            }
        }
    }

    private bool TryAdvanceSlide(DateTime started, DateTime now)
    {
        if (_config.SlideSeconds <= 0 || _playlist.Count <= 1) return false;
        int period = Math.Max(1, _config.SlideSeconds);
        int next = (int)((now - started).TotalSeconds / period) % _playlist.Count;
        if (next == _slideIndex) return false;
        RebuildRenderer(now, started);
        return true;
    }

    private void RebuildRenderer(DateTime now, DateTime? started = null)
    {
        _playlist = _config.ResolvePlaylist(_baseDir);
        string? path = null;
        if (_playlist.Count > 0)
        {
            if (_config.SlideSeconds > 0 && _playlist.Count > 1)
            {
                int period = Math.Max(1, _config.SlideSeconds);
                var origin = started ?? now;
                _slideIndex = (int)((now - origin).TotalSeconds / period) % _playlist.Count;
            }
            else
                _slideIndex = Math.Clamp(_slideIndex, 0, _playlist.Count - 1);

            string media = _playlist[_slideIndex];
            path = _config.UseMedia(media) ? media : null;
        }

        var next = new DashboardRenderer(_panel!.Width, _panel.Height, path, _config);

        lock (_gate)
        {
            _renderer?.Dispose();
            _renderer = next;
        }

        string slide = _playlist.Count > 1 ? $" {_slideIndex + 1}/{_playlist.Count}" : "";
        _status = path is null
            ? "Estadísticas"
            : $"Media{slide} ({_config.Rotate}° {_config.Fit} x{_config.Zoom:0.##})";
        Log($"OK PM={_panel.Pm} SUB={_panel.Sub} {_panel.Width}x{_panel.Height} mode={_status} media={path ?? "(none)"}");
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
    }

    private void DisposePanel()
    {
        lock (_gate)
        {
            _renderer?.Dispose();
            _renderer = null;
            _panel?.Dispose();
            _panel = null;
        }
    }

    private void Exit()
    {
        _cts.Cancel();
        _watcher?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        DisposePanel();
        _stats.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _watcher?.Dispose();
            _tray.Dispose();
            DisposePanel();
            _stats.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string Truncate(string text, int max = 60) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "runtime.log"),
                $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
