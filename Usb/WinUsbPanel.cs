using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ThermalrightVision.Usb;

public sealed class WinUsbPanel : IDisposable
{
    public const ushort VendorId = 0x87AD;
    public const ushort ProductId = 0x70DB;

    private static Guid UsbDeviceInterface() => new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private SafeFileHandle? _device;
    private IntPtr _winUsb = IntPtr.Zero;

    public int Width { get; private set; } = 1600;
    public int Height { get; private set; } = 720;
    public byte Pm { get; private set; }
    public byte Sub { get; private set; }

    public void Open((int Width, int Height)? overrideSize = null)
    {
        string path = FindDevicePath()
            ?? throw new InvalidOperationException(
                "No esta conectada la pantalla USB 87AD:70DB. Cierra TRCC y vuelve a conectar el USB.");

        // WinUSB exige FILE_FLAG_OVERLAPPED (0x40000000) aunque las lecturas/escrituras sean sincronas.
        const uint GenericReadWrite = 0xC0000000;
        const uint FileShareReadWrite = 0x00000003;
        const uint OpenExisting = 3;
        const uint FileFlagOverlapped = 0x40000000;

        try
        {
            _device = CreateFile(
                path,
                GenericReadWrite,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExisting,
                FileFlagOverlapped,
                IntPtr.Zero);

            if (_device.IsInvalid)
                throw new InvalidOperationException($"No se pudo abrir {path}. Error {Marshal.GetLastWin32Error()}.");

            if (!WinUsb_Initialize(_device, out _winUsb))
                throw new InvalidOperationException($"WinUsb_Initialize fallo en {path}. Error {Marshal.GetLastWin32Error()}.");

            byte[] handshake = Protocol.Handshake();
            WritePipe(0x01, handshake);
            byte[] reply = ReadPipe(0x81, 1024);
            (Pm, Sub) = Protocol.ParseHandshake(reply);
            var size = overrideSize ?? Protocol.PanelSize(Pm, Sub);
            Width = size.Width;
            Height = size.Height;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SendJpeg(ReadOnlySpan<byte> jpeg)
    {
        byte[] packet = Protocol.JpegPacket(jpeg, Width, Height);
        for (int offset = 0; offset < packet.Length; offset += Protocol.Chunk)
        {
            int len = Math.Min(Protocol.Chunk, packet.Length - offset);
            WritePipe(0x01, packet.AsSpan(offset, len));
        }
        if (packet.Length % 512 == 0)
            WritePipe(0x01, ReadOnlySpan<byte>.Empty);
    }

    public void Dispose()
    {
        if (_winUsb != IntPtr.Zero)
        {
            WinUsb_Free(_winUsb);
            _winUsb = IntPtr.Zero;
        }
        _device?.Dispose();
    }

    private void WritePipe(byte pipe, ReadOnlySpan<byte> data)
    {
        if (_winUsb == IntPtr.Zero) throw new ObjectDisposedException(nameof(WinUsbPanel));
        unsafe
        {
            fixed (byte* ptr = data)
            {
                if (!WinUsb_WritePipe(_winUsb, pipe, (IntPtr)ptr, (uint)data.Length, out uint written, IntPtr.Zero))
                    throw new InvalidOperationException($"WritePipe fallo ({Marshal.GetLastWin32Error()}).");
            }
        }
    }

    private byte[] ReadPipe(byte pipe, int max)
    {
        if (_winUsb == IntPtr.Zero) throw new ObjectDisposedException(nameof(WinUsbPanel));
        var buffer = new byte[max];
        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                if (!WinUsb_ReadPipe(_winUsb, pipe, (IntPtr)ptr, (uint)buffer.Length, out uint read, IntPtr.Zero))
                    throw new InvalidOperationException($"ReadPipe fallo ({Marshal.GetLastWin32Error()}).");
                if (read == buffer.Length) return buffer;
                return buffer.AsSpan(0, (int)read).ToArray();
            }
        }
    }

    private static string? FindDevicePath()
    {
        Guid iface = UsbDeviceInterface();
        IntPtr list = SetupDiGetClassDevs(ref iface, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (list == new IntPtr(-1)) return null;
        try
        {
            var data = new SpDeviceInterfaceData { Size = Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(list, IntPtr.Zero, ref iface, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(list, ref data, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var info = new SpDevInfoData { Size = Marshal.SizeOf<SpDevInfoData>() };
                    if (!SetupDiGetDeviceInterfaceDetail(list, ref data, detail, needed, out _, ref info))
                        continue;
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    if (path.Contains("VID_87AD", StringComparison.OrdinalIgnoreCase) &&
                        path.Contains("PID_70DB", StringComparison.OrdinalIgnoreCase))
                        return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(list);
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public int Size;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, out uint lengthTransferred, IntPtr overlapped);
}
