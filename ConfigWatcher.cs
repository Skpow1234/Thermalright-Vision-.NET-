namespace ThermalrightVision;

public sealed class ConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Action _onChanged;
    private readonly object _gate = new();
    private CancellationTokenSource? _debounce;

    public ConfigWatcher(string baseDir, Action onChanged, bool enabled)
    {
        _onChanged = onChanged;
        if (!enabled) return;
        string path = AppConfig.ConfigPath(baseDir);
        string? dir = Path.GetDirectoryName(path);
        string name = Path.GetFileName(path);
        if (dir is null || string.IsNullOrWhiteSpace(name)) return;

        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        _watcher.Changed += (_, _) => Debounce();
        _watcher.Created += (_, _) => Debounce();
        _watcher.Renamed += (_, _) => Debounce();
        _watcher.EnableRaisingEvents = true;
    }

    private void Debounce()
    {
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, token);
                    if (!token.IsCancellationRequested)
                        _onChanged();
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _watcher?.Dispose();
    }
}
