using Musix.Audio;
using Xunit;

namespace Musix.Tests;

public class AudioSessionEnumeratorTests
{
    [Fact]
    public void GetActiveSessions_ReturnsNonNull()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        Assert.NotNull(sessions);
    }

    [Fact]
    public void GetActiveSessions_AllSessionsHavePositivePid()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        Assert.All(sessions, s => Assert.True(s.ProcessId > 0));
    }

    [Fact]
    public void GetActiveSessions_AllSessionsHaveNonEmptyProcessName()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        Assert.All(sessions, s => Assert.False(string.IsNullOrEmpty(s.ProcessName)));
    }

    [Fact]
    public void GetActiveSessions_NoDuplicatePids()
    {
        IReadOnlyList<AudioSession> sessions = AudioSessionEnumerator.GetActiveSessions();
        IEnumerable<int> pids = sessions.Select(s => s.ProcessId);
        Assert.Equal(pids.Count(), pids.Distinct().Count());
    }
}
