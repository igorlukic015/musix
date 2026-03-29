using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

MMDeviceEnumerator enumerator = new();
MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine($"default audio endpoint name: {device.FriendlyName}");

SessionCollection sessions = device.AudioSessionManager.Sessions;
for (int i = 0; i < sessions.Count; i++)
{
    AudioSessionControl session = sessions[i];

    if (session.GetProcessID == 0 || session.State != AudioSessionState.AudioSessionStateActive)
    {
        continue;
    }

    string processName = Process.GetProcessById((int)session.GetProcessID).ProcessName;

    Console.WriteLine($"Process: {processName}");
}
