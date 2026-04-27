namespace Musix.Network;

public static class PacketReader
{
    public static async Task<AudioPacket> ReadAsync(Stream stream)
    {
        byte[] header = new byte[18];
        await stream.ReadExactlyAsync(header);

        ushort packetType = BitConverter.ToUInt16(header, 0);
        uint sequenceNumber = BitConverter.ToUInt32(header, 2);
        long timestampTicks = BitConverter.ToInt64(header, 6);
        int payloadLength = BitConverter.ToInt32(header, 14);

        byte[] payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload);

        return new AudioPacket(packetType, sequenceNumber, timestampTicks, payload);
    }
}
