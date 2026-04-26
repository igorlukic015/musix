using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

TcpListener listener = new(IPAddress.Loopback, 5000);
listener.Start();
Console.WriteLine("Listening on port 5000, waiting for a connection...");

using TcpClient client = await listener.AcceptTcpClientAsync();
Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}\n");

NetworkStream stream = client.GetStream();
Random rng = new();
byte[] lengthPrefix = new byte[4];

for (int i = 0; i < 10; i++)
{
    int size = rng.Next(100, 4001);
    byte[] payload = new byte[size];
    rng.NextBytes(payload);

    BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, size);
    await stream.WriteAsync(lengthPrefix);
    await stream.WriteAsync(payload);

    Console.WriteLine($"  Sent message {i + 1,2}: {size,5} bytes");
}

Console.WriteLine("\nDone.");
