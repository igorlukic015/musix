namespace Musix.Network;

public sealed class JitterBuffer
{
    private readonly SortedDictionary<uint, AudioPacket> _buffer = new();
    private readonly object _lock = new();
    private uint _nextSeq;

    public void Add(AudioPacket packet)
    {
        lock (_lock)
            _buffer[packet.SequenceNumber] = packet;
    }

    public AudioPacket? TryDequeue()
    {
        lock (_lock)
        {
            uint seq = _nextSeq++;
            if (_buffer.Remove(seq, out AudioPacket? packet))
                return packet;
            Console.WriteLine($"gap at sequence {seq}");
            return null;
        }
    }
}
