using System;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public enum EditorOperationKind
{
    Command,
    Query,
    PreviewCommand,
    PreviewQuery
}

public enum EditorOperationScope
{
    File,
    Track,
    Note,
    Drum,
    Guitar,
    Arrangement,
    AutoEdit,
    Preview
}

public enum HistoryPolicy
{
    None,
    CaptureIfChanged,
    AlwaysCapture
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorOperationAttribute : Attribute
{
    public EditorOperationAttribute(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public EditorOperationKind Kind { get; init; } = EditorOperationKind.Command;
    public EditorOperationScope Scope { get; init; } = EditorOperationScope.File;
    public string MenuPath { get; init; }
    public int SortOrder { get; init; }
    public bool RequiresFile { get; init; } = true;
    public bool RequiresSelectedTracks { get; init; }
    public bool RequiresSelectedEvents { get; init; }
    public HistoryPolicy HistoryPolicy { get; init; } = HistoryPolicy.CaptureIfChanged;
}

public sealed record EditorOperationDescriptor(
    string Id,
    string DisplayName,
    EditorOperationKind Kind,
    EditorOperationScope Scope,
    string MenuPath,
    int SortOrder,
    bool RequiresFile,
    bool RequiresSelectedTracks,
    bool RequiresSelectedEvents,
    HistoryPolicy HistoryPolicy)
{
    public static EditorOperationDescriptor FromType(Type operationType)
    {
        ArgumentNullException.ThrowIfNull(operationType);

        var attribute = Attribute.GetCustomAttribute(
            operationType,
            typeof(EditorOperationAttribute)) as EditorOperationAttribute;

        if (attribute is null)
            throw new InvalidOperationException(
                $"{operationType.FullName} is missing {nameof(EditorOperationAttribute)}.");

        return FromAttribute(attribute);
    }

    public static EditorOperationDescriptor FromAttribute(EditorOperationAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        return new EditorOperationDescriptor(
            attribute.Id,
            attribute.DisplayName,
            attribute.Kind,
            attribute.Scope,
            attribute.MenuPath,
            attribute.SortOrder,
            attribute.RequiresFile,
            attribute.RequiresSelectedTracks,
            attribute.RequiresSelectedEvents,
            attribute.HistoryPolicy);
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EditorOperationPresenterAttribute : Attribute
{
    public EditorOperationPresenterAttribute(string operationId)
    {
        OperationId = operationId;
    }

    public string OperationId { get; }
}
