using Musix.Audio;
using NAudio.Wave;

IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();

if (sessions.Count == 0)
{
    Console.WriteLine("No active audio sessions found. Play some audio and try again.");
    return;
}

for (int i = 0; i < sessions.Count; i++)
{
    Console.WriteLine($"  [{i}] {sessions[i].ProcessName} (PID {sessions[i].ProcessId})");
}

Console.Write("\nEnter index to capture (or press Enter for 0): ");
string? input = Console.ReadLine();
int selectedIndex = int.TryParse(input, out int parsed) ? parsed : 0;

AudioSession target = sessions[selectedIndex];
Console.WriteLine($"\nActivating process loopback for: {target.ProcessName} (PID {target.ProcessId})");

using ProcessLoopbackCapture capture = new();
await capture.InitializeAsync(target.ProcessId);

Console.WriteLine($"Format: {capture.WaveFormat}");

int totalBytes = 0;
capture.DataAvailable += (object? _, WaveInEventArgs e) =>
{
    totalBytes += e.BytesRecorded;
    Console.Write($"\rCaptured: {totalBytes / 1024} KB  ");
};

capture.StartCapture();
Console.WriteLine("Capturing... Press Enter to stop.");
Console.ReadLine();
capture.StopCapture();

Console.WriteLine($"\nDone. Total captured: {totalBytes / 1024} KB");
