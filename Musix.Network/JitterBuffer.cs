namespace Musix.Network;

public sealed class JitterBuffer
{
    private readonly SortedDictionary<uint, AudioPacket> _buffer = new();
    private readonly object _lock = new();
    private readonly int _targetDepth;
    private uint _nextSeq;
    private bool _primed;

    public JitterBuffer(int targetDepth = 5)
    {
        _targetDepth = targetDepth;
    }

    public void Add(AudioPacket packet)
    {
        lock (_lock)
            _buffer[packet.SequenceNumber] = packet;
    }

    public AudioPacket? TryDequeue()
    {
        lock (_lock)
        {
            if (!_primed)
            {
                if (_buffer.Count < _targetDepth)
                    return null;
                _primed = true;
                _nextSeq = _buffer.Keys.First();
            }

            uint seq = _nextSeq++;
            if (_buffer.Remove(seq, out AudioPacket? packet))
                return packet;
            Console.WriteLine($"gap at sequence {seq}");
            return null;
        }
    }
}
