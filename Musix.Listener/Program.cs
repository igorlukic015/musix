using System.Net.Sockets;

Console.WriteLine("Connecting to localhost:5000...");

using TcpClient client = new();
await client.ConnectAsync("127.0.0.1", 5000);
Console.WriteLine("Connected.");

using StreamReader reader = new(client.GetStream());

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    while (!cts.IsCancellationRequested)
    {
        string? line = await reader.ReadLineAsync(cts.Token);
        if (line is null)
        {
            Console.WriteLine("Connection closed by host.");
            break;
        }
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
    }
}
catch (OperationCanceledException) { }

Console.WriteLine("Done.");
