using System.Runtime.InteropServices;

namespace ThermalrightVision.Sensors;

public sealed class Stats
{
    public double? CpuPercent { get; init; }
    public double? CpuMhz { get; init; }
    public double? GpuTemp { get; init; }
    public double? GpuHotspot { get; init; }
    public double? GpuPercent { get; init; }
    public double? RamPercent { get; init; }
    public string GpuName { get; init; } = "GPU";
}

public sealed class StatsReader : IDisposable
{
    private long _idle;
    private long _total;
    private IntPtr _adl;
    private IntPtr _ctx;
    private int? _adapter;
    private string _gpuName = "GPU";
    private AdlQuery? _query;
    private bool _ready;
    private AdlAlloc? _allocKeepAlive;

    private delegate IntPtr AdlAlloc(int size);
    private delegate int AdlCreate(AdlAlloc alloc, int enumConnected, out IntPtr context);
    private delegate int AdlDestroy(IntPtr context);
    private delegate int AdlNumber(IntPtr context, out int count);
    private delegate int AdlInfo(IntPtr context, IntPtr info, int size);
    private delegate int AdlQuery(IntPtr context, int adapterIndex, out AdlPmLog log);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PnpString;
        public int OsDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlSensor
    {
        public int Supported;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPmLog
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public AdlSensor[] Sensors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Lo;
        public uint Hi;
        public long Value => ((long)Hi << 32) | Lo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInfo
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr inBuf, int inLen, [Out] ProcessorPowerInfo[] outBuf, int outLen);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadLibrary(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalAlloc(uint flags, UIntPtr bytes);

    public StatsReader()
    {
        OpenAdl();
        SampleCpu();
    }

    public Stats Sample()
    {
        var (cpu, mhz) = SampleCpu();
        return new Stats
        {
            CpuPercent = cpu,
            CpuMhz = mhz,
            GpuTemp = Sensor(8, Celsius),
            GpuHotspot = Sensor(27, Celsius),
            GpuPercent = Sensor(19, Percent),
            RamPercent = SampleRam(),
            GpuName = _gpuName
        };
    }

    public void Dispose()
    {
        if (_ready && _ctx != IntPtr.Zero)
        {
            var destroy = Marshal.GetDelegateForFunctionPointer<AdlDestroy>(GetProcAddress(_adl, "ADL2_Main_Control_Destroy"));
            destroy(_ctx);
        }
    }

    private (double? cpu, double? mhz) SampleCpu()
    {
        double? percent = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            long idleNow = idle.Value;
            long totalNow = kernel.Value + user.Value;
            if (_total > 0)
            {
                long idleDelta = idleNow - _idle;
                long totalDelta = totalNow - _total;
                if (totalDelta > 0)
                    percent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
            }
            _idle = idleNow;
            _total = totalNow;
        }
        return (percent, SampleMhz());
    }

    private static double? SampleMhz()
    {
        uint count = GetActiveProcessorCount(0xFFFF);
        if (count == 0) return null;
        var buffer = new ProcessorPowerInfo[count];
        if (CallNtPowerInformation(11, IntPtr.Zero, 0, buffer, Marshal.SizeOf<ProcessorPowerInfo>() * (int)count) != 0)
            return null;
        var clocks = buffer.Where(x => x.CurrentMhz > 0).Select(x => (double)x.CurrentMhz).ToArray();
        return clocks.Length == 0 ? null : clocks.Average();
    }

    private static double? SampleRam()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : null;
    }

    private void OpenAdl()
    {
        _adl = LoadLibrary("atiadlxx.dll");
        if (_adl == IntPtr.Zero) return;
        _allocKeepAlive = size => LocalAlloc(0x0040, (UIntPtr)size);
        var create = Marshal.GetDelegateForFunctionPointer<AdlCreate>(GetProcAddress(_adl, "ADL2_Main_Control_Create"));
        if (create(_allocKeepAlive, 1, out _ctx) != 0) return;

        var number = Marshal.GetDelegateForFunctionPointer<AdlNumber>(GetProcAddress(_adl, "ADL2_Adapter_NumberOfAdapters_Get"));
        number(_ctx, out int count);
        if (count <= 0) return;

        int size = Marshal.SizeOf<AdapterInfo>();
        IntPtr buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var blank = new AdapterInfo { Size = size };
                Marshal.StructureToPtr(blank, buffer + i * size, false);
            }
            var infoGet = Marshal.GetDelegateForFunctionPointer<AdlInfo>(GetProcAddress(_adl, "ADL2_Adapter_AdapterInfo_Get"));
            if (infoGet(_ctx, buffer, size * count) != 0) return;

            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<AdapterInfo>(buffer + i * size);
                string name = info.AdapterName ?? "";
                if (name.Contains("9070", StringComparison.OrdinalIgnoreCase) || name.Contains("RX ", StringComparison.OrdinalIgnoreCase))
                {
                    _adapter = info.AdapterIndex;
                    _gpuName = name;
                    break;
                }
                if (_adapter is null)
                {
                    _adapter = info.AdapterIndex;
                    _gpuName = name;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        _query = Marshal.GetDelegateForFunctionPointer<AdlQuery>(GetProcAddress(_adl, "ADL2_New_QueryPMLogData_Get"));
        _ready = _adapter is not null && _query is not null;
    }

    private double? Sensor(int index, Func<int, double> scale)
    {
        if (!_ready || _adapter is null || _query is null) return null;
        if (_query(_ctx, _adapter.Value, out var log) != 0 || log.Sensors is null) return null;
        var sensor = log.Sensors[index];
        return sensor.Supported == 0 ? null : scale(sensor.Value);
    }

    private static double Celsius(int raw) => raw > 200 ? raw / 1000.0 : raw;
    private static double Percent(int raw) => raw > 100 ? raw / 100.0 : raw;
}
