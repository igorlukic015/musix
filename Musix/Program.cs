using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

MMDeviceEnumerator enumerator = new();
MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine($"Default audio endpoint: {device.FriendlyName}");
Console.WriteLine();

SessionCollection sessions = device.AudioSessionManager.Sessions;
List<(int Pid, string Name)> activeSessions = [];

for (int i = 0; i < sessions.Count; i++)
{
    AudioSessionControl session = sessions[i];

    if (session.GetProcessID == 0 || session.State != AudioSessionState.AudioSessionStateActive)
    {
        continue;
    }

    int pid = (int)session.GetProcessID;
    string processName = Process.GetProcessById(pid).ProcessName;
    activeSessions.Add((pid, processName));
    Console.WriteLine($"  [{activeSessions.Count - 1}] {processName} (PID {pid})");
}

if (activeSessions.Count == 0)
{
    Console.WriteLine("No active audio sessions found. Play some audio and try again.");
    return;
}

Console.Write("\nEnter index to capture (or press Enter for 0): ");
string? input = Console.ReadLine();
int selectedIndex = int.TryParse(input, out int parsed) ? parsed : 0;

(int targetPid, string targetName) = activeSessions[selectedIndex];
Console.WriteLine($"\nActivating process loopback for: {targetName} (PID {targetPid})");

using Musix.ProcessLoopbackCapture capture = new();
await capture.InitializeAsync(targetPid);

Console.WriteLine($"Format: {capture.WaveFormat}");

int totalBytes = 0;
capture.DataAvailable += (object? _, NAudio.Wave.WaveInEventArgs e) =>
{
    totalBytes += e.BytesRecorded;
    Console.Write($"\rCaptured: {totalBytes / 1024} KB  ");
};

capture.StartCapture();
Console.WriteLine("Capturing... Press Enter to stop.");
Console.ReadLine();
capture.StopCapture();

Console.WriteLine($"\nDone. Total captured: {totalBytes / 1024} KB");
