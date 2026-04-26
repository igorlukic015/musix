using Musix.Audio;
using NAudio.Lame;
using NAudio.Wave;
using Xunit;

namespace Musix.Tests;

public sealed class CaptureToMp3Tests
{
    [SkippableFact]
    public async Task CaptureAudio_TenSeconds_WritesToMp3File()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        Skip.If(sessions.Count == 0, "No active audio sessions — start an application that plays audio before running this test.");

        for (int i = 0; i < sessions.Count; i++)
        {
            Console.WriteLine($"  [{i}] {sessions[i].ProcessName} (PID {sessions[i].ProcessId})");
        }

        Console.Write("\nEnter index to capture (or press Enter for 0): ");
        Console.Out.Flush();
        string? input = Console.ReadLine();
        int selectedIndex = int.TryParse(input, out int parsed) ? parsed : 0;

        AudioSession target = sessions[selectedIndex];

        using ProcessLoopbackCapture capture = new();
        await capture.InitializeAsync(target.ProcessId);

        string outputPath = Path.GetFullPath($"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.mp3");

        Console.WriteLine($"Capturing {target.ProcessName} (PID {target.ProcessId}) for 10 seconds...");
        Console.WriteLine($"Output: {outputPath}");
        Console.Out.Flush();

        WaveFormat lameFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            capture.WaveFormat.SampleRate,
            capture.WaveFormat.Channels);
        using LameMP3FileWriter mp3Writer = new(outputPath, lameFormat, LAMEPreset.STANDARD);

        capture.DataAvailable += (object? _, WaveInEventArgs e) =>
        {
            if (e.BytesRecorded > 0)
            {
                mp3Writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        };

        capture.StartCapture();
        await Task.Delay(TimeSpan.FromSeconds(10));
        capture.StopCapture();

        Console.WriteLine($"Done. Saved to: {outputPath}");
    }
}
