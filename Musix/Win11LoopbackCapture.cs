using Windows.Media.Audio;
using Windows.Media.Render;

namespace Musix;

internal sealed class Win11LoopbackCapture : IDisposable
{
    private AudioGraph? _graph;
    private AudioLoopbackNode? _loopbackNode;

    public async Task InitializeAsync()
    {
        AudioGraphSettings settings = new(AudioRenderCategory.Media);
        CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

        if (result.Status != AudioGraphCreationStatus.Success)
        {
            throw new InvalidOperationException(
                $"AudioGraph creation failed with status: {result.Status}");
        }

        _graph = result.Graph;
    }

    public async Task CreateLoopbackNodeAsync(int targetPid)
    {
        if (_graph is null)
        {
            throw new InvalidOperationException(
                "AudioGraph is not initialized. Call InitializeAsync() first.");
        }

        AudioLoopbackNodeCreationOptions options = new() { ProcessId = (uint)targetPid };
        CreateAudioLoopbackNodeResult result = await _graph.CreateLoopbackNodeAsync(options);

        if (result.Status != AudioLoopbackNodeCreationStatus.Success)
        {
            throw new InvalidOperationException(
                $"Loopback node creation failed with status: {result.Status}");
        }

        _loopbackNode = result.Node;
    }

    public void Dispose()
    {
        _loopbackNode?.Dispose();
        _loopbackNode = null;
        _graph?.Dispose();
        _graph = null;
    }
}
