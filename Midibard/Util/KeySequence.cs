using System;
using System.Linq;
using System.Collections.Generic;

using Dalamud.Bindings.ImGui;

namespace MidiBard.Util;

public class KeySequence
{
    private readonly int[] _sequence;
    private readonly int[] _uniqueKeys;

    private int _index = 0;
    public bool IsUnlocked { get; private set; }

    private readonly byte[] _keyboardState = new byte[256];
    private readonly byte[] _prevState = new byte[256];

    // Debug
    private readonly Queue<int> _recentKeys = new();
    private const int DebugHistorySize = 10;

    private static readonly int[] DefaultSequence =
    {
        // ↑ ↑ ↓ ↓ ← → ← → B A Enter
        0x26,
        0x26,
        0x28,
        0x28,
        0x25,
        0x27,
        0x25,
        0x27,
        0x42,
        0x41,
        0x0D
    };

    public KeySequence(int[]? sequence = null)
    {
        _sequence = sequence ?? DefaultSequence;
        _uniqueKeys = _sequence.Distinct().ToArray();
    }

    public void Reset()
    {
        _index = 0;
        IsUnlocked = false;
        Array.Clear(_prevState, 0, _prevState.Length);
        _recentKeys.Clear();
    }

    public void Update()
    {
        if (IsUnlocked)
            return;

        WindowsApi.GetKeyboardState(_keyboardState);

        foreach (var key in _uniqueKeys)
        {
            if (IsKeyPressed(key))
            {
                AddDebugKey(key);

                if (key == _sequence[_index])
                {
                    _index++;
                }
                else if (key == _sequence[0])
                {
                    _index = 1;
                }
                else
                {
                    _index = 0;
                }

                if (_index >= _sequence.Length)
                {
                    IsUnlocked = true;
                    _index = 0;
                }

                break;
            }
        }

        Buffer.BlockCopy(_keyboardState, 0, _prevState, 0, 256);
    }

    private void AddDebugKey(int key)
    {
        _recentKeys.Enqueue(key);
        if (_recentKeys.Count > DebugHistorySize)
            _recentKeys.Dequeue();
    }

    private bool IsKeyPressed(int key)
    {
        bool current = (_keyboardState[key] & 0x80) != 0;
        bool previous = (_prevState[key] & 0x80) != 0;
        return current && !previous;
    }

    public void DrawDebug()
    {
        ImGui.Text("=== Key Sequence Debug ===");

        ImGui.Text($"Unlocked: {(IsUnlocked ? "YES" : "NO")}");
        ImGui.Text($"Progress: {_index}/{_sequence.Length}");

        ImGui.Separator();

        ImGui.Text("Sequence:");
        for (int i = 0; i < _sequence.Length; i++)
        {
            var key = _sequence[i];

            if (i < _index)
                ImGui.TextColored(Style.Colors.Green, GetKeyName(key));
            else if (i == _index)
                ImGui.TextColored(Style.Colors.Red, $"> {GetKeyName(key)}");
            else
                ImGui.Text(GetKeyName(key));
        }

        ImGui.Separator();

        ImGui.Text("Recent Inputs:");
        foreach (var key in _recentKeys)
        {
            ImGui.SameLine();
            ImGui.Text(GetKeyName(key));
        }

        ImGui.NewLine();

        if (ImGui.Button("Reset"))
        {
            Reset();
        }
    }

    private static string GetKeyName(int key)
    {
        return key switch
        {
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            0x0D => "Enter",
            _ => ((char)key).ToString()
        };
    }
}
