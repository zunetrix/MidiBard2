using System.Reflection;

using MidiBard.Control.MidiControl.Editing;
using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.Tests.Control.MidiControl.Editing.Commands;

public class CommandArchitectureTests
{
    [Fact]
    public void EditingServicesDoNotOwnUserFacingOperationResults()
    {
        var offenders = typeof(MidiForgeOperations).Assembly
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
        var registry = EditorCommandRegistry.Discover(typeof(MidiForgeOperations).Assembly);

        registry.Operations.ShouldNotBeEmpty();
    }
}
