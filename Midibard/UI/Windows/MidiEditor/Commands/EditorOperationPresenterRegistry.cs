using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MidiBard.Control.MidiControl.Editing.Commands;

namespace MidiBard.UI.Windows.MidiEditor.Commands;

public sealed class EditorOperationPresenterRegistry
{
    private readonly Dictionary<string, Func<IEditorOperationPresenter>> presenters;

    private EditorOperationPresenterRegistry(IEnumerable<EditorOperationPresenterRegistration> registrations)
    {
        presenters = registrations.ToDictionary(
            registration => registration.OperationId,
            registration => registration.Factory,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<string> OperationIds
        => presenters.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public static EditorOperationPresenterRegistry Discover(
        EditorCommandRegistry commandRegistry,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromTypes(
            commandRegistry,
            assemblies.SelectMany(assembly => assembly.GetTypes()));
    }

    public static EditorOperationPresenterRegistry FromTypes(
        EditorCommandRegistry commandRegistry,
        params Type[] presenterTypes)
        => FromTypes(commandRegistry, (IEnumerable<Type>)presenterTypes);

    public static EditorOperationPresenterRegistry FromTypes(
        EditorCommandRegistry commandRegistry,
        IEnumerable<Type> presenterTypes)
    {
        ArgumentNullException.ThrowIfNull(presenterTypes);

        var registrations = new List<EditorOperationPresenterRegistration>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in presenterTypes)
        {
            if (type is null || type.IsAbstract || type.IsInterface)
                continue;

            var attribute = Attribute.GetCustomAttribute(
                type,
                typeof(EditorOperationPresenterAttribute)) as EditorOperationPresenterAttribute;

            if (attribute is null)
                continue;

            ValidateAttribute(type, attribute);

            if (!typeof(IEditorOperationPresenter).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"{type.FullName} must implement {nameof(IEditorOperationPresenter)}.");
            }

            if (commandRegistry is not null && !commandRegistry.Contains(attribute.OperationId))
            {
                throw new InvalidOperationException(
                    $"{type.FullName} targets unknown editor operation '{attribute.OperationId}'.");
            }

            if (!seenIds.Add(attribute.OperationId))
                throw new InvalidOperationException($"Duplicate presenter for editor operation '{attribute.OperationId}'.");

            registrations.Add(new EditorOperationPresenterRegistration(
                attribute.OperationId,
                CreateFactory(type)));
        }

        return new EditorOperationPresenterRegistry(registrations);
    }

    public bool TryGetPresenter(string operationId, out IEditorOperationPresenter presenter)
    {
        if (!presenters.TryGetValue(operationId, out var factory))
        {
            presenter = null;
            return false;
        }

        presenter = factory();
        return true;
    }

    public IEditorOperationPresenter GetPresenter(string operationId)
    {
        if (!TryGetPresenter(operationId, out var presenter))
            throw new KeyNotFoundException($"No presenter registered for editor operation '{operationId}'.");

        return presenter;
    }

    private static Func<IEditorOperationPresenter> CreateFactory(Type type)
    {
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (constructor is null)
            throw new InvalidOperationException($"{type.FullName} must have a parameterless constructor.");

        return () => (IEditorOperationPresenter)constructor.Invoke(null);
    }

    private static void ValidateAttribute(Type type, EditorOperationPresenterAttribute attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute.OperationId))
            throw new InvalidOperationException($"{type.FullName} has an empty editor operation presenter id.");
    }

    private sealed record EditorOperationPresenterRegistration(
        string OperationId,
        Func<IEditorOperationPresenter> Factory);
}
