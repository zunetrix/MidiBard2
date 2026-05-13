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
                actualEnsembleModeRunning: false,
                ensemblePlayEnabled: true,
                playOnMultipleDevices: true)
            .ShouldBeTrue();
    }

    [Fact]
    public void ShouldPersist_ActualEnsembleWithoutParty_ReturnsFalse()
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: false,
                isPartyLeader: false,
                actualEnsembleModeRunning: true,
                ensemblePlayEnabled: true,
                playOnMultipleDevices: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldPersist_InPartyWithoutMultiClientPlayback_ReturnsTrue()
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: false,
                actualEnsembleModeRunning: false,
                ensemblePlayEnabled: false,
                playOnMultipleDevices: false)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldPersist_ActualEnsemblePartyLeader_ReturnsTrue(bool ensemblePlayEnabled, bool playOnMultipleDevices)
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: true,
                actualEnsembleModeRunning: true,
                ensemblePlayEnabled,
                playOnMultipleDevices)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldPersist_ActualEnsemblePartyNonLeader_ReturnsFalse(bool ensemblePlayEnabled, bool playOnMultipleDevices)
    {
        PlaybackCompletionPersistencePolicy.ShouldPersist(
                isInParty: true,
                isPartyLeader: false,
                actualEnsembleModeRunning: true,
                ensemblePlayEnabled,
                playOnMultipleDevices)
            .ShouldBeFalse();
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
                actualEnsembleModeRunning: false,
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
                actualEnsembleModeRunning: false,
                ensemblePlayEnabled,
                playOnMultipleDevices)
            .ShouldBeFalse();
    }
}
