namespace MidiBard.Tests.UI.Windows.MidiEditor;

public class MidiEditorPreviewCameraTests
{
    [Fact]
    public void FollowPlayback_DoesNotScrollBeforeMidpoint()
    {
        var cameraTime = MidiEditorPreviewCamera.FollowPlayback(
            cameraTime: 10,
            playbackPosition: 14.9,
            visibleTime: 10,
            maxTime: 100);

        cameraTime.ShouldBe(10);
    }

    [Fact]
    public void FollowPlayback_KeepsCursorAroundMiddleAfterMidpoint()
    {
        var cameraTime = MidiEditorPreviewCamera.FollowPlayback(
            cameraTime: 10,
            playbackPosition: 18,
            visibleTime: 10,
            maxTime: 100);

        cameraTime.ShouldBe(13);
    }

    [Fact]
    public void FollowPlayback_BringsCursorBackIntoViewWhenBehindCamera()
    {
        var cameraTime = MidiEditorPreviewCamera.FollowPlayback(
            cameraTime: 10,
            playbackPosition: 8,
            visibleTime: 10,
            maxTime: 100);

        cameraTime.ShouldBe(3);
    }

    [Fact]
    public void EnsureVisible_OnlyScrollsWhenOutsideVisibleRange()
    {
        MidiEditorPreviewCamera.EnsureVisible(10, 15, 10, 100).ShouldBe(10);
        MidiEditorPreviewCamera.EnsureVisible(10, 25, 10, 100).ShouldBe(20);
        MidiEditorPreviewCamera.EnsureVisible(10, 2, 10, 100).ShouldBe(0);
    }

    [Fact]
    public void CameraHelpers_ClampAndExposeExpectedPreviewTooltips()
    {
        MidiEditorPreviewCamera.Clamp(-10, 100).ShouldBe(0);
        MidiEditorPreviewCamera.Clamp(120, 100).ShouldBe(100);
        MidiEditorPreviewCamera.GetVisibleTime(250, 25).ShouldBe(10);

        MidiEditorPreviewControlTooltips.RestartPreview.ShouldBe("Restart Preview");
        MidiEditorPreviewControlTooltips.ResumePreview.ShouldBe("Resume Preview");
        MidiEditorPreviewControlTooltips.PausePreview.ShouldBe("Pause Preview");
        MidiEditorPreviewControlTooltips.StopPreview.ShouldBe("Stop Preview");
    }
}
