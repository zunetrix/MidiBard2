using System;

using Dalamud.Interface;
using Dalamud.Interface.Utility;

#nullable enable

namespace MidiBard;

/// <summary>
/// Reusable component for displaying temporary messages/notifications in ImGui windows.
/// Automatically clears messages after a specified duration.
/// </summary>
public class ImGuiMessageDisplay
{
    private string _message = string.Empty;
    private DateTime _messageTime = DateTime.MinValue;
    private readonly int _displayDurationMs;

    /// <summary>
    /// Creates a new message display component with auto-clear duration.
    /// </summary>
    /// <param name="displayDurationMs">Duration in milliseconds to display the message (default: 5000)</param>
    public ImGuiMessageDisplay(int displayDurationMs = 5000)
    {
        _displayDurationMs = displayDurationMs;
    }

    /// <summary>
    /// Show a message for the configured duration.
    /// </summary>
    /// <param name="message">The message text to display</param>
    public void Show(string message)
    {
        _message = message;
        _messageTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Check if there's an active message currently being displayed.
    /// </summary>
    public bool HasMessage => !string.IsNullOrEmpty(_message) &&
        (DateTime.UtcNow - _messageTime).TotalMilliseconds < _displayDurationMs;

    /// <summary>
    /// Manually clear the current message.
    /// </summary>
    public void Clear()
    {
        _message = string.Empty;
    }

    /// <summary>
    /// Draw the message banner if active. Call this in your window's Draw() method.
    /// </summary>
    public void Draw()
    {
        if (HasMessage)
        {
            ImGuiUtil.DrawColoredBanner(_message, Style.Colors.Red);
        }
    }

    /// <summary>
    /// Draw the message banner with custom color if active.
    /// </summary>
    /// <param name="color">The color to draw the banner with (Vector4)</param>
    public void Draw(System.Numerics.Vector4 color)
    {
        if (HasMessage)
        {
            ImGuiUtil.DrawColoredBanner(_message, color);
        }
    }
}
