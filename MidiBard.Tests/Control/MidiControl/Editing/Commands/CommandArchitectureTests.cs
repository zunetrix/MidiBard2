using System.Reflection;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class CommandArchitectureTests
{
    private static readonly string[] ForbiddenMidiEditorWindowMutationPatterns =
    {
        "CaptureHistorySnapshot(",
        "BeginPendingCapture(",
        "CommitPendingCapture(",
        "ExecuteDirectEdit(",
        ".CloneTrack(",
        ".RemoveTrack(",
        ".MoveTrack(",
        ".SplitTrackByChannel(",
        ".TransposeTracks(",
        ".MergeTracks(",
        ".QuantizeTracks(",
        ".QuantizeNotes(",
        ".SanitizeFile(",
        ".ImportTracksFromFile(",
        ".Save(",
        ".SaveAs(",
        "new EditableMidiFile(",
        ".ConsolidateTempoToConductorTrack(",
        ".SetDirtyStateForLoad(",
        ".MarkClean(",
        ".MergeMultipleConductorTracks(",
        ".InsertNote(",
        ".RemoveEvent(",
        ".SetChannel(",
        ".ApplyEditValues(",
        ".MarkNameDirty(",
        ".MarkChanged(",
    };

    private static readonly KnownRemainingUiMutation[] KnownMidiEditorWindowAdapterBoundaries =
    {
        new(".LoadEvents(", 2),
        new(".UnloadEvents(", 1),
        new(".FlushChanges(", 1),
    };

    [Fact]
    public void EditingServicesDoNotOwnUserFacingOperationResults()
    {
        var offenders = typeof(EditorCommandExecutor).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.Contains(".Editing") == true)
            .Where(type => type.Name.EndsWith("Service"))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.ReturnType.Name.StartsWith("MidiForge")
                                 && method.ReturnType.Name.EndsWith("Result"))
                .Select(method => $"{type.FullName}.{method.Name} returns {method.ReturnType.Name}"))
            .OrderBy(offender => offender)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void ProductionEditorOperationsFollowMetadataConventions()
    {
        var registry = EditorCommandRegistry.Discover(typeof(EditorCommandExecutor).Assembly);

        registry.Operations.ShouldNotBeEmpty();
    }

    [Fact]
    public void MidiEditorWindowDoesNotUseCommandWorthyMutationPatterns()
    {
        var source = ReadMidiEditorWindowSource();

        var offenders = ForbiddenMidiEditorWindowMutationPatterns
            .Select(pattern => new
            {
                Pattern = pattern,
                ActualOccurrences = CountOccurrences(source, pattern),
            })
            .Where(known => known.ActualOccurrences > 0)
            .Select(known => $"{known.Pattern}: expected 0, found {known.ActualOccurrences}")
            .OrderBy(offender => offender)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void MidiEditorWindowAdapterBoundaryAllowlistDoesNotGrow()
    {
        var source = ReadMidiEditorWindowSource();

        var offenders = KnownMidiEditorWindowAdapterBoundaries
            .Select(known => new
            {
                known.Pattern,
                known.MaximumOccurrences,
                ActualOccurrences = CountOccurrences(source, known.Pattern),
            })
            .Where(known => known.ActualOccurrences > known.MaximumOccurrences)
            .Select(known => $"{known.Pattern}: expected at most {known.MaximumOccurrences}, found {known.ActualOccurrences}")
            .OrderBy(offender => offender)
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    private static string ReadMidiEditorWindowSource()
    {
        var editorDirectory = Path.Combine(
            FindRepositoryRoot(),
            "Midibard",
            "UI",
            "Windows",
            "MidiEditor");

        return string.Join(
            "\n",
            Directory
                .EnumerateFiles(editorDirectory, "MidiEditorWindow*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path)
                .Select(File.ReadAllText));
    }

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
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

    private sealed record KnownRemainingUiMutation(
        string Pattern,
        int MaximumOccurrences);
}
