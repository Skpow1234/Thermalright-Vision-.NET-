using System.Runtime.InteropServices;
using System.Text;

namespace ThermalrightVision.Sensors;

/// <summary>
/// Lee la temp de CPU desde la shared memory de HWiNFO (si la app está abierta
/// con "Shared Memory Support"). Cero dependencias; si no hay mapa, devuelve null.
/// </summary>
public static class HwinfoCpuTemp
{
    private const string MapName = "Global\\HWiNFO_SENS_SM2";
    private const uint FileMapRead = 0x0004;

    public static double? TryRead()
    {
        IntPtr map = OpenFileMappingW(FileMapRead, false, MapName);
        if (map == IntPtr.Zero) return null;
        IntPtr view = IntPtr.Zero;
        try
        {
            view = MapViewOfFile(map, FileMapRead, 0, 0, UIntPtr.Zero);
            if (view == IntPtr.Zero) return null;

            uint signature = (uint)Marshal.ReadInt32(view);
            // "SiWH" little-endian = 0x48576953, some builds use HWiS
            if (signature is not (0x48576953 or 0x53695748)) return null;

            uint readingOffset = (uint)Marshal.ReadInt32(view, 0x20);
            uint readingSize = (uint)Marshal.ReadInt32(view, 0x24);
            uint readingCount = (uint)Marshal.ReadInt32(view, 0x28);
            if (readingSize < 0x124 || readingCount == 0 || readingCount > 4096) return null;

            double? best = null;
            int bestScore = -1;
            for (uint i = 0; i < readingCount; i++)
            {
                IntPtr row = view + (int)(readingOffset + i * readingSize);
                int type = Marshal.ReadInt32(row); // 1 = TEMP
                if (type != 1) continue;
                string label = ReadFixed(row + 0x0C, 128) + " " + ReadFixed(row + 0x8C, 128);
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(row, 0x11C));
                if (value is <= 0 or > 120) continue;

                int score = Score(label);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = value;
                }
            }
            return bestScore >= 0 ? best : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (view != IntPtr.Zero) UnmapViewOfFile(view);
            CloseHandle(map);
        }
    }

    private static int Score(string label)
    {
        string l = label.ToLowerInvariant();
        if (!l.Contains("cpu") && !l.Contains("tctl") && !l.Contains("tdie") && !l.Contains("ryzen") && !l.Contains("package"))
            return -1;
        if (l.Contains("tctl") || l.Contains("tdie") || l.Contains("package")) return 100;
        if (l.Contains("cpu") && l.Contains("temp")) return 80;
        if (l.Contains("cpu")) return 50;
        return 10;
    }

    private static string ReadFixed(IntPtr ptr, int max)
    {
        var bytes = new byte[max];
        Marshal.Copy(ptr, bytes, 0, max);
        int n = Array.IndexOf(bytes, (byte)0);
        if (n < 0) n = max;
        return Encoding.ASCII.GetString(bytes, 0, n);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr view);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
