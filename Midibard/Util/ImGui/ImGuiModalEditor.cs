using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace MidiBard;

/// <summary>
/// Generic modal dialog for editing any data type
/// Allows rendering custom content for different data types
/// </summary>
public class ImGuiModalEditor<T> where T : class
{
    private readonly string _id;
    private string _title = "Edit";
    private T? _data;
    private bool _isOpen = false;
    private Action<T>? _onSave;
    private Action<ImGuiModalEditor<T>, T>? _contentRenderer;
    private Vector2 _minSize;

    public ImGuiModalEditor(string id = "##ModalEditor", Vector2? minSize = null)
    {
        _id = id;
        _minSize = minSize ?? ImGuiHelpers.ScaledVector2(400, 150);
    }

    /// <summary>
    /// Show modal with custom content renderer
    /// </summary>
    /// <param name="title">Modal title</param>
    /// <param name="data">Data to edit</param>
    /// <param name="contentRenderer">Function that renders the content inside the modal</param>
    /// <param name="onSave">Callback when Save is clicked (data is passed to callback)</param>
    public void Show(string title, T data, Action<ImGuiModalEditor<T>, T> contentRenderer, Action<T> onSave)
    {
        _title = title;
        _data = data;
        _contentRenderer = contentRenderer;
        _onSave = onSave;
        _isOpen = true;
    }

    /// <summary>
    /// Show modal with optional custom min size
    /// </summary>
    public void Show(string title, T data, Vector2 minSize, Action<ImGuiModalEditor<T>, T> contentRenderer, Action<T> onSave)
    {
        _title = title;
        _data = data;
        _contentRenderer = contentRenderer;
        _onSave = onSave;
        _minSize = minSize;
        _isOpen = true;
    }

    public void Draw()
    {
        if (_isOpen)
        {
            ImGui.OpenPopup($"{_title}##{_id}");
            _isOpen = false;
        }

        var viewport = ImGui.GetMainViewport();
        Vector2 center = viewport.GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(_minSize, new Vector2(float.MaxValue, float.MaxValue));

        if (ImGui.BeginPopupModal($"{_title}##{_id}", ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Render custom content
            if (_data != null && _contentRenderer != null)
            {
                _contentRenderer.Invoke(this, _data);
            }

            ImGui.NewLine();
            ImGui.Separator();
            ImGui.Spacing();

            // Center buttons
            float buttonWidth = 100f;
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float totalWidth = (2 * buttonWidth) + spacing;
            float availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
            float offsetX = (availableWidth - totalWidth) / 2.0f;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

            if (ImGui.Button("Save##ModalSave", new Vector2(buttonWidth, 0)))
            {
                if (_data != null)
                {
                    _onSave?.Invoke(_data);
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel##ModalCancel", new Vector2(buttonWidth, 0)))
            {
                ImGui.CloseCurrentPopup();
                _data = null;
            }

            ImGui.EndPopup();
        }
    }
}
