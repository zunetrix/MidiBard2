using System.Linq;

using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace MidiBard.Control.MidiControl.Editing.Commands.Conductor;

internal static class ConductorCommandHelpers
{
    /// <summary>
    /// Finds the existing conductor track or creates a new empty one at index 0.
    /// Flushes all tracks before searching so pending TimedObjectsManager state is written back.
    /// </summary>
    public static (EditableTrack conductor, bool created) FindOrCreateConductorTrack(EditableMidiFile file)
    {
        file.FlushAllTracks();

        var conductor = file.Tracks.FirstOrDefault(track => track.IsConductorTrack);
        if (conductor is not null)
            return (conductor, false);

        conductor = new EditableTrack(new TrackChunk(), 0);
        file.Tracks.Insert(0, conductor);
        ReindexTracks(file);
        return (conductor, true);
    }

    /// <summary>
    /// After creating a conductor track and adding events, the EditableTrack's
    /// IsConductorTrack flag is stale (it was false at construction when the chunk
    /// was empty). Flushing, rebuilding source chunks, and reloading from source
    /// reconstructs all tracks so the flag is correct.
    /// </summary>
    public static void ReloadTracksForConductorFlag(EditableMidiFile file)
    {
        file.FlushAllTracks();
        file.RebuildSourceChunksFromTracks();
        file.ReloadTracksFromSource();
    }

    /// <summary>Refresh hints for a conductor-track mutation. Reloads track list only when a conductor was created.</summary>
    public static EditorRefreshHints ConductorChangedHints(bool createdConductor) => new(
        ReloadTrackList: createdConductor,
        ReloadSelectedTrack: true,
        RebuildPreview: true,
        RecalculateMetrics: true);

    private static void ReindexTracks(EditableMidiFile file)
    {
        for (var i = 0; i < file.Tracks.Count; i++)
            file.Tracks[i].Index = i;
    }
}
