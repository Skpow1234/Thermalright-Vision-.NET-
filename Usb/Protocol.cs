using System.Buffers.Binary;

namespace ThermalrightVision.Usb;

public static class Protocol
{
    public const int Magic = unchecked((int)0x78563412); // wire: 12 34 56 78
    public const int CmdJpeg = 2;
    public const int HeaderLen = 64;
    public const int Chunk = 16 * 1024;

    public static readonly Dictionary<(byte Pm, byte Sub), (int W, int H)> Panels = new()
    {
        [(64, 1)] = (1600, 720)
    };

    public static byte[] Handshake()
    {
        var packet = new byte[HeaderLen];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0x38, 4), 1);
        return packet;
    }

    public static (byte Pm, byte Sub) ParseHandshake(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < 37 || reply[0] != 0x12 || reply[1] != 0x34 || reply[2] != 0x56 || reply[3] != 0x78)
            throw new InvalidOperationException($"Respuesta de handshake no valida ({reply.Length} bytes).");
        return (reply[24], reply[36]);
    }

    public static (int Width, int Height) PanelSize(byte pm, byte sub) =>
        Panels.TryGetValue((pm, sub), out var size) ? size : (1600, 720);

    public static byte[] JpegPacket(ReadOnlySpan<byte> jpeg, int width, int height)
    {
        var packet = new byte[HeaderLen + jpeg.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), CmdJpeg);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12, 4), height);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0x38, 4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0x3C, 4), jpeg.Length);
        jpeg.CopyTo(packet.AsSpan(HeaderLen));
        return packet;
    }
}
