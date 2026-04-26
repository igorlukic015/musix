using System.Buffers.Binary;
using System.Net.Sockets;

Console.WriteLine("Connecting to localhost:5000...");

using TcpClient client = new();
await client.ConnectAsync("127.0.0.1", 5000);
Console.WriteLine("Connected.\n");

NetworkStream stream = client.GetStream();
byte[] lengthBuffer = new byte[4];

for (int i = 0; i < 10; i++)
{
    await stream.ReadExactlyAsync(lengthBuffer);
    int expected = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);

    byte[] payload = new byte[expected];
    await stream.ReadExactlyAsync(payload);

    Console.WriteLine($"  Received message {i + 1,2}: {payload.Length,5} bytes (expected {expected,5}) — {(payload.Length == expected ? "OK" : "MISMATCH")}");
}

Console.WriteLine("\nDone.");
