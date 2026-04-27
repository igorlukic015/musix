using Musix.Network;
using System.Net;
using System.Net.Sockets;

namespace Musix.Host;

public sealed class AudioBroadcaster : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<NetworkStream> _clients = new();
    private readonly object _clientsLock = new();
    private uint _sequenceNumber;

    public AudioBroadcaster(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        _listener.Start();
        _ = AcceptLoopAsync(cancellationToken);
    }

    public void SendFrame(byte[] opusData)
    {
        uint seq = _sequenceNumber++;
        AudioPacket packet = new(1, seq, DateTime.UtcNow.Ticks, opusData);

        List<NetworkStream> snapshot;
        lock (_clientsLock)
            snapshot = [.._clients];

        List<NetworkStream>? toRemove = null;
        foreach (NetworkStream stream in snapshot)
        {
            try
            {
                PacketWriter.Write(stream, packet);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                toRemove ??= new();
                toRemove.Add(stream);
            }
        }

        if (toRemove is not null)
        {
            lock (_clientsLock)
                foreach (NetworkStream s in toRemove)
                    _clients.Remove(s);
        }

        Console.WriteLine($"seq={seq} size={18 + opusData.Length}B clients={snapshot.Count}");
    }

    public void Dispose()
    {
        _listener.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                NetworkStream stream = client.GetStream();

                lock (_clientsLock)
                    _clients.Add(stream);

                Console.WriteLine($"listener connected: {client.Client.RemoteEndPoint}");
                _ = MonitorClientAsync(client, stream);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task MonitorClientAsync(TcpClient client, NetworkStream stream)
    {
        string? endpoint = client.Client.RemoteEndPoint?.ToString();
        try
        {
            byte[] buffer = new byte[1];
            while (await stream.ReadAsync(buffer) > 0) { }
        }
        catch (IOException) { }
        finally
        {
            lock (_clientsLock)
                _clients.Remove(stream);

            stream.Dispose();
            client.Dispose();
            Console.WriteLine($"listener disconnected: {endpoint}");
        }
    }
}
