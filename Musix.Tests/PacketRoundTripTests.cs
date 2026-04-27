using Musix.Network;
using Xunit;

namespace Musix.Tests;

public sealed class PacketRoundTripTests
{
    [Fact]
    public async Task WriteAndRead_ReturnsIdenticalPacket()
    {
        AudioPacket original = new(
            PacketType: 0x0001,
            SequenceNumber: 42u,
            TimestampTicks: 637_000_000_000L,
            Payload: [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF]);

        using MemoryStream stream = new();
        PacketWriter.Write(stream, original);
        stream.Position = 0;
        AudioPacket result = await PacketReader.ReadAsync(stream);

        Assert.Equal(original.PacketType, result.PacketType);
        Assert.Equal(original.SequenceNumber, result.SequenceNumber);
        Assert.Equal(original.TimestampTicks, result.TimestampTicks);
        Assert.Equal(original.Payload, result.Payload);
    }

    [Fact]
    public async Task WriteAndRead_EmptyPayload_ReturnsIdenticalPacket()
    {
        AudioPacket original = new(
            PacketType: 0x0000,
            SequenceNumber: 0u,
            TimestampTicks: 0L,
            Payload: []);

        using MemoryStream stream = new();
        PacketWriter.Write(stream, original);
        stream.Position = 0;
        AudioPacket result = await PacketReader.ReadAsync(stream);

        Assert.Equal(original.PacketType, result.PacketType);
        Assert.Equal(original.SequenceNumber, result.SequenceNumber);
        Assert.Equal(original.TimestampTicks, result.TimestampTicks);
        Assert.Equal(original.Payload, result.Payload);
    }

    [Fact]
    public async Task WriteAndRead_MaxFieldValues_ReturnsIdenticalPacket()
    {
        byte[] payload = new byte[1024];
        new Random(42).NextBytes(payload);

        AudioPacket original = new(
            PacketType: ushort.MaxValue,
            SequenceNumber: uint.MaxValue,
            TimestampTicks: long.MaxValue,
            Payload: payload);

        using MemoryStream stream = new();
        PacketWriter.Write(stream, original);
        stream.Position = 0;
        AudioPacket result = await PacketReader.ReadAsync(stream);

        Assert.Equal(original.PacketType, result.PacketType);
        Assert.Equal(original.SequenceNumber, result.SequenceNumber);
        Assert.Equal(original.TimestampTicks, result.TimestampTicks);
        Assert.Equal(original.Payload, result.Payload);
    }
}
