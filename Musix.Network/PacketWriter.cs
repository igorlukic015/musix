using System.Text;

namespace Musix.Network;

public static class PacketWriter
{
    public static void Write(Stream stream, AudioPacket packet)
    {
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(packet.PacketType);
        writer.Write(packet.SequenceNumber);
        writer.Write(packet.TimestampTicks);
        writer.Write(packet.Payload.Length);
        writer.Write(packet.Payload);
    }
}
