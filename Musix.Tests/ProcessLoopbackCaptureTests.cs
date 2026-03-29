using Musix.Audio;
using NAudio.Wave;
using Xunit;

namespace Musix.Tests;

public class ProcessLoopbackCaptureTests
{
    // ── No audio required ────────────────────────────────────────────────────

    [Fact]
    public void WaveFormat_BeforeInitialize_ThrowsInvalidOperationException()
    {
        using ProcessLoopbackCapture capture = new();
        Assert.Throws<InvalidOperationException>(() => _ = capture.WaveFormat);
    }

    [Fact]
    public void StartCapture_BeforeInitialize_ThrowsInvalidOperationException()
    {
        using ProcessLoopbackCapture capture = new();
        Assert.Throws<InvalidOperationException>(() => capture.StartCapture());
    }

    [Fact]
    public void Dispose_WithoutInitialize_DoesNotThrow()
    {
        ProcessLoopbackCapture capture = new();
        Exception? ex = Record.Exception(() => capture.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void StopCapture_WithoutStarting_DoesNotThrow()
    {
        using ProcessLoopbackCapture capture = new();
        Exception? ex = Record.Exception(() => capture.StopCapture());
        Assert.Null(ex);
    }

    // ── Requires active audio process (skipped if none found) ────────────────

    [SkippableFact]
    public async Task InitializeAsync_SetsValidWaveFormat()
    {
        int pid = GetActiveAudioPid();

        using ProcessLoopbackCapture capture = new();
        await capture.InitializeAsync(pid);

        WaveFormat format = capture.WaveFormat;
        Assert.True(format.SampleRate > 0, "SampleRate must be positive.");
        Assert.True(format.Channels > 0, "Channels must be positive.");
        Assert.True(format.BitsPerSample > 0, "BitsPerSample must be positive.");
        Assert.True(format.BlockAlign > 0, "BlockAlign must be positive.");
    }

    [SkippableFact]
    public async Task StartCapture_FiresDataAvailableWithBytes()
    {
        int pid = GetActiveAudioPid();

        using ProcessLoopbackCapture capture = new();
        await capture.InitializeAsync(pid);

        int totalBytes = 0;
        capture.DataAvailable += (object? _, WaveInEventArgs e) => totalBytes += e.BytesRecorded;

        capture.StartCapture();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        capture.StopCapture();

        Assert.True(totalBytes > 0, "Expected captured audio data but received 0 bytes after 500ms.");
    }

    [SkippableFact]
    public async Task CapturedData_EachPacketAlignedToBlockAlign()
    {
        int pid = GetActiveAudioPid();

        using ProcessLoopbackCapture capture = new();
        await capture.InitializeAsync(pid);

        int blockAlign = capture.WaveFormat.BlockAlign;
        List<int> packetSizes = [];
        capture.DataAvailable += (object? _, WaveInEventArgs e) => packetSizes.Add(e.BytesRecorded);

        capture.StartCapture();
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        capture.StopCapture();

        Skip.If(packetSizes.Count == 0, "No packets received — audio may have been silent during the test.");
        Assert.All(packetSizes, size => Assert.Equal(0, size % blockAlign));
    }

    [SkippableFact]
    public async Task Dispose_AfterCapturing_DoesNotThrow()
    {
        int pid = GetActiveAudioPid();

        ProcessLoopbackCapture capture = new();
        await capture.InitializeAsync(pid);
        capture.StartCapture();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        capture.StopCapture();

        Exception? ex = Record.Exception(() => capture.Dispose());
        Assert.Null(ex);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int GetActiveAudioPid()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        Skip.If(sessions.Count == 0, "No active audio sessions — start an application that plays audio before running this test.");
        return sessions[0].ProcessId;
    }
}
