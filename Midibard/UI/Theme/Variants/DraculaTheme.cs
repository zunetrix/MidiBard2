using System.Numerics;

namespace MidiBard;

public class DraculaTheme : UITheme
{
    public Vector4 Text { get; init; } = new Vector4(0.972f, 0.972f, 0.949f, 1f);          // #f8f8f2
    public Vector4 TextDisabled { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);  // #6272a4
    public Vector4 WindowBg { get; init; } = new Vector4(0.157f, 0.165f, 0.212f, 1f);      // #282a36
    public Vector4 ChildBg { get; init; } = new Vector4(0.157f, 0.165f, 0.212f, 1f);
    public Vector4 PopupBg { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);       // #44475a
    public Vector4 Border { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 BorderShadow { get; init; } = new Vector4(0f, 0f, 0f, 0f);
    public Vector4 FrameBg { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 FrameBgHovered { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 FrameBgActive { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f); // #bd93f9
    public Vector4 TitleBg { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 TitleBgActive { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 TitleBgCollapsed { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f); // #21222c
    public Vector4 MenuBarBg { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f);
    public Vector4 ScrollbarBg { get; init; } = new Vector4(0.157f, 0.165f, 0.212f, 1f);
    public Vector4 ScrollbarGrab { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 ScrollbarGrabHovered { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 ScrollbarGrabActive { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f); // #ff79c6
    public Vector4 CheckMark { get; init; } = new Vector4(0.314f, 0.980f, 0.482f, 1f);     // #50fa7b
    public Vector4 SliderGrab { get; init; } = new Vector4(0.545f, 0.914f, 0.992f, 1f);    // #8be9fd
    public Vector4 SliderGrabActive { get; init; } = new Vector4(0.314f, 0.980f, 0.482f, 1f);
    public Vector4 Button { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 ButtonHovered { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 ButtonActive { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 Header { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 HeaderHovered { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 HeaderActive { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f);
    public Vector4 Separator { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 SeparatorHovered { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 SeparatorActive { get; init; } = new Vector4(1.0f, 0.722f, 0.439f, 1f); // #ffb86c
    public Vector4 ResizeGrip { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 ResizeGripHovered { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 ResizeGripActive { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f);
    public Vector4 Tab { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 TabHovered { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 TabActive { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f);
    public Vector4 TabUnfocused { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f);
    public Vector4 TabUnfocusedActive { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 DockingPreview { get; init; } = new Vector4(0.314f, 0.980f, 0.482f, 0.5f);
    public Vector4 DockingEmptyBg { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f);
    public Vector4 PlotLines { get; init; } = new Vector4(0.972f, 0.972f, 0.949f, 1f);
    public Vector4 PlotLinesHovered { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f);
    public Vector4 PlotHistogram { get; init; } = new Vector4(1.0f, 0.722f, 0.439f, 1f);
    public Vector4 PlotHistogramHovered { get; init; } = new Vector4(1.0f, 0.475f, 0.776f, 1f);
    public Vector4 TableHeaderBg { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 TableBorderStrong { get; init; } = new Vector4(0.384f, 0.447f, 0.643f, 1f);
    public Vector4 TableBorderLight { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 TableRowBg { get; init; } = new Vector4(0.157f, 0.165f, 0.212f, 1f);
    public Vector4 TableRowBgAlt { get; init; } = new Vector4(0.204f, 0.216f, 0.275f, 1f); // #343746
    public Vector4 TextSelectedBg { get; init; } = new Vector4(0.267f, 0.278f, 0.357f, 1f);
    public Vector4 DragDropTarget { get; init; } = new Vector4(1.0f, 0.722f, 0.439f, 1f);
    public Vector4 NavHighlight { get; init; } = new Vector4(0.741f, 0.576f, 0.976f, 1f);
    public Vector4 NavWindowingHighlight { get; init; } = new Vector4(0.972f, 0.972f, 0.949f, 1f);
    public Vector4 NavWindowingDimBg { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f);
    public Vector4 ModalWindowDimBg { get; init; } = new Vector4(0.129f, 0.133f, 0.173f, 1f);
}
