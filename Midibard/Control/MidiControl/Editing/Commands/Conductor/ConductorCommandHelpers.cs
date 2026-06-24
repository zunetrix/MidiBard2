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
        conductor.MarkAsConductorTrack();
        file.Tracks.Insert(0, conductor);
        ReindexTracks(file);
        return (conductor, true);
    }

    /// <summary>
    /// After adding events to a conductor track, flush pending changes, rebuild source
    /// chunks from tracks, and reload all tracks from source.  This ensures the
    /// conductor track chunk is fully incorporated and that the EditableTrack
    /// auto-detection of IsConductorTrack reflects the actual chunk content.
    /// Most callers no longer need this because FindOrCreateConductorTrack calls
    /// MarkAsConductorTrack() on creation, so the flag is correct immediately.
    /// Used primarily when events are added to a track that already existed but
    /// whose chunk has not yet been flushed/committed to the source.
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
