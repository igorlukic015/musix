using System.Net;
using System.Net.Sockets;

TcpListener listener = new(IPAddress.Loopback, 5000);
listener.Start();
Console.WriteLine("Listening on port 5000, waiting for a connection...");

using TcpClient client = await listener.AcceptTcpClientAsync();
Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

await using StreamWriter writer = new(client.GetStream(), leaveOpen: true) { AutoFlush = true };

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    while (!cts.IsCancellationRequested)
    {
        await writer.WriteLineAsync("hello");
        await Task.Delay(1000, cts.Token);
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("Done.");
