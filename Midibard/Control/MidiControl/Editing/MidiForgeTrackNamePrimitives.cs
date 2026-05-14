using System;
using System.Collections.Generic;
using System.Linq;

namespace MidiBard.Control.MidiControl.Editing;

internal static class MidiForgeTrackNamePrimitives
{
    private static readonly HashSet<string> PreservedDrumTrackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BassDrum",
        "SnareDrum",
        "Cymbal",
        "Bongo",
        "Timpani",
        "Drumkit",
    };

    public static bool IsPreservedDrumTrackName(string name)
        => PreservedDrumTrackNames.Contains(name);

    public static int[] GetValidPerformanceTrackIndices(
        EditableMidiFile file,
        IEnumerable<int> trackIndices)
        => trackIndices
            .Where(index => index >= 0 && index < file.Tracks.Count && !file.Tracks[index].IsConductorTrack)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();

    public static bool SetEditableTrackName(EditableTrack track, string name)
    {
        if (string.Equals(track.Name, name, StringComparison.Ordinal))
            return false;

        track.Name = name;
        track.MarkNameDirty();
        return true;
    }
}
