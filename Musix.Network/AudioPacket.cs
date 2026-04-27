namespace Musix.Network;

public record AudioPacket(
    ushort PacketType,
    uint SequenceNumber,
    long TimestampTicks,
    byte[] Payload);
