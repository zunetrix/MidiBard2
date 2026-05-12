using MidiBard.Playlist.Helpers;

namespace MidiBard.Tests.Playlist;

public class PlaybackCompletionPersistencePolicyTests
{
    [Fact]
    public void ShouldPersist_NotInParty_ReturnsTrue()
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: false,
                isPartyLeader: false,
                ensemblePlayEnabled: true,
                playOnMultipleDevices: true)
            .ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_InPartyWithoutMultiClientPlayback_ReturnsTrue()
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: false,
                ensemblePlayEnabled: false,
                playOnMultipleDevices: false)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldPersist_MultiClientPartyLeader_ReturnsTrue(bool ensemblePlayEnabled, bool playOnMultipleDevices)
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: true,
                ensemblePlayEnabled,
                playOnMultipleDevices)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldPersist_MultiClientPartyNonLeader_ReturnsFalse(bool ensemblePlayEnabled, bool playOnMultipleDevices)
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: false,
                ensemblePlayEnabled,
                playOnMultipleDevices)
            .ShouldBeFalse();
    }
}
