using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

namespace Musix.Audio;

internal static class AudioSessionEnumerator
{
    public static IReadOnlyList<AudioSession> GetActiveSessions()
    {
        MMDeviceEnumerator deviceEnumerator = new();
        MMDevice device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        SessionCollection sessions = device.AudioSessionManager.Sessions;

        List<AudioSession> activeSessions = [];

        for (int i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl session = sessions[i];

            if (session.GetProcessID == 0 || session.State != AudioSessionState.AudioSessionStateActive)
            {
                continue;
            }

            int processId = (int)session.GetProcessID;
            string processName = Process.GetProcessById(processId).ProcessName;
            activeSessions.Add(new AudioSession(processId, processName));
        }

        return activeSessions;
    }
}
