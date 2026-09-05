using MidiBard.Playlist;
using MidiBard.RemoteControl;
using PlaylistModel = MidiBard.Playlist.Playlist;

namespace MidiBard.Tests.RemoteControl;

public class RemoteControlSongLookupTests
{
    [Fact]
    public void ExactLookupMatchesBasenameCaseInsensitively()
    {
        var playlist = PlaylistWith(
            "/music/Chrono Trigger - Frog's Theme.mid",
            "/music/Chrono Trigger - Wind Scene.mid");

        var matches = RemoteControlService.FindExactFileNameMatches(
            playlist,
            "chrono trigger - frog's theme.MID");

        matches.ShouldBe(new[] { 0 });
    }

    [Fact]
    public void ExactLookupDoesNotAcceptSubstringMatches()
    {
        var playlist = PlaylistWith(
            "/music/Chrono Trigger - Frog's Theme.mid",
            "/music/Chrono Trigger - Frog's Theme Remix.mid");

        RemoteControlService.FindExactFileNameMatches(
                playlist,
                "Frog's Theme.mid")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExactLookupReturnsAllDuplicateBasenamesSoCallerCanRejectAmbiguity()
    {
        var playlist = PlaylistWith(
            "/music/a/Test.mid",
            "/music/b/Test.mid",
            "/music/Other.mid");

        RemoteControlService.FindExactFileNameMatches(playlist, "Test.mid")
            .ShouldBe(new[] { 0, 1 });
    }

    private static PlaylistModel PlaylistWith(params string[] paths)
    {
        return new PlaylistModel
        {
            Name = "Recording",
            Songs = paths
                .Select(path => new PlaylistSong
                {
                    Song = new Song
                    {
                        FilePath = path,
                        Name = Path.GetFileNameWithoutExtension(path),
                    },
                })
                .ToList(),
        };
    }
}
