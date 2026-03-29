using NAudio.CoreAudioApi;

MMDeviceEnumerator enumerator = new();
MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine(device.FriendlyName);

SessionCollection sessions = device.AudioSessionManager.Sessions;
for (int i = 0; i < sessions.Count; i++)
{
    AudioSessionControl session = sessions[i];
    Console.WriteLine(session.GetProcessID);
}
