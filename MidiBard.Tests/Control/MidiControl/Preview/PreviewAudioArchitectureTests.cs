using MidiBard.Control.MidiControl.Editing.Commands;
using MidiBard.Control.MidiControl.Preview;

namespace MidiBard.Tests.Control.MidiControl.Preview;

public class PreviewAudioArchitectureTests
{
    [Fact]
    public void RuntimePreviewSoundPlayerUsesCommandLayerSoundBoundary()
    {
        typeof(IEditorPreviewSoundPlayer)
            .IsAssignableFrom(typeof(IMidiEditorPreviewSoundPlayer))
            .ShouldBeTrue();

        var player = EmptyEditorPreviewSoundPlayer.Instance;
        var sound = player.Play(
            new PreviewSoundRequest(0, 0, 60, 12, 2),
            out var statusMessage);

        sound.ShouldBe(0);
        statusMessage.ShouldBeNull();
    }

    [Fact]
    public void PreviewPlaybackDoesNotCallLowLevelSoundApisDirectly()
    {
        var previewDirectory = Path.Combine(
            FindRepositoryRoot(),
            "Midibard",
            "Control",
            "MidiControl",
            "Preview");
        var allowedLowLevelFiles = new HashSet<string>
        {
            "MidiEditorPlaybackPreviewAdapters.cs",
            "PerformanceSampleCatalog.cs",
            "PerformanceSampleProbe.cs",
        };
        var lowLevelPatterns = new[]
        {
            "FFXIVClientStructs.FFXIV.Client.Sound",
            "SoundManager.Instance",
            "->PlaySound",
            "SoundData*",
        };

        var offenders = Directory
            .EnumerateFiles(previewDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !allowedLowLevelFiles.Contains(Path.GetFileName(path)))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return lowLevelPatterns
                    .Where(source.Contains)
                    .Select(pattern => $"{Path.GetRelativePath(previewDirectory, path)} contains {pattern}");
            })
            .OrderBy(offender => offender)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Midibard")) &&
                Directory.Exists(Path.Combine(current.FullName, "MidiBard.Tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
