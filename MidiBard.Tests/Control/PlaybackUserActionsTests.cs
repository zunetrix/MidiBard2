using MidiBard.Control;

namespace MidiBard.Tests.Control;

public class PlaybackUserActionsTests
{
    [Theory]
    [InlineData(false, 8, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, true)]
    [InlineData(true, 8, true)]
    public void ChatPlaylistSyncMatchesUiSelectionBehavior(
        bool playOnMultipleDevices,
        int partyMemberCount,
        bool expected)
    {
        PlaybackUserActions.UseChatPlaylistSync(
                playOnMultipleDevices,
                partyMemberCount)
            .ShouldBe(expected);
    }
}
