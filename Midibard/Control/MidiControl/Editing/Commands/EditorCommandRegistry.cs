using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MidiBard.Control.MidiControl.Editing.Commands;

public sealed class EditorCommandRegistry
{
    private readonly Dictionary<string, EditorOperationRegistration> registrations;
    private readonly IReadOnlyList<EditorOperationDescriptor> operations;
    private readonly Dictionary<string, IReadOnlyList<EditorOperationDescriptor>> menuOperations;

    private EditorCommandRegistry(IEnumerable<EditorOperationRegistration> registrations)
    {
        this.registrations = registrations.ToDictionary(
            registration => registration.Descriptor.Id,
            StringComparer.Ordinal);

        operations = this.registrations.Values
            .Select(registration => registration.Descriptor)
            .OrderBy(descriptor => descriptor.MenuPath, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.SortOrder)
            .ThenBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
            .ToArray();

        menuOperations = operations
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.MenuPath))
            .GroupBy(descriptor => descriptor.MenuPath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EditorOperationDescriptor>)group.ToArray(),
                StringComparer.Ordinal);
    }

    public IReadOnlyList<EditorOperationDescriptor> Operations => operations;

    public static EditorCommandRegistry Discover(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromTypes(assemblies.SelectMany(assembly => assembly.GetTypes()));
    }

    public static EditorCommandRegistry FromTypes(params Type[] operationTypes)
        => FromTypes((IEnumerable<Type>)operationTypes);

    public static EditorCommandRegistry FromTypes(IEnumerable<Type> operationTypes)
    {
        ArgumentNullException.ThrowIfNull(operationTypes);

        var registrations = new List<EditorOperationRegistration>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in operationTypes)
        {
            if (type is null || type.IsAbstract || type.IsInterface)
                continue;

            var attribute = Attribute.GetCustomAttribute(
                type,
                typeof(EditorOperationAttribute)) as EditorOperationAttribute;

            if (attribute is null)
                continue;

            ValidateAttribute(type, attribute);
            var contract = ResolveOperationContract(type);
            if (contract.Kind != attribute.Kind)
            {
                throw new InvalidOperationException(
                    $"{type.FullName} declares {attribute.Kind} but implements {contract.Kind}.");
            }

            if (!seenIds.Add(attribute.Id))
                throw new InvalidOperationException($"Duplicate editor operation id '{attribute.Id}'.");

            registrations.Add(new EditorOperationRegistration(
                EditorOperationDescriptor.FromAttribute(attribute),
                type,
                contract.Kind,
                contract.OptionsType,
                contract.ResultType,
                CreateFactory(type)));
        }

        return new EditorCommandRegistry(registrations);
    }

    public IEditorCommand<TOptions, TResult> GetCommand<TOptions, TResult>(string id)
        => GetOperation<IEditorCommand<TOptions, TResult>>(id, EditorOperationKind.Command);

    public IEditorQuery<TOptions, TResult> GetQuery<TOptions, TResult>(string id)
        => GetOperation<IEditorQuery<TOptions, TResult>>(id, EditorOperationKind.Query);

    public IPreviewCommand<TOptions, TResult> GetPreviewCommand<TOptions, TResult>(string id)
        => GetOperation<IPreviewCommand<TOptions, TResult>>(id, EditorOperationKind.PreviewCommand);

    public IPreviewQuery<TOptions, TResult> GetPreviewQuery<TOptions, TResult>(string id)
        => GetOperation<IPreviewQuery<TOptions, TResult>>(id, EditorOperationKind.PreviewQuery);

    public bool Contains(string id)
        => registrations.ContainsKey(id);

    public IReadOnlyList<EditorOperationDescriptor> GetMenuOperations(string menuPath)
        => menuOperations.TryGetValue(menuPath, out var descriptors)
            ? descriptors
            : [];

    private TOperation GetOperation<TOperation>(string id, EditorOperationKind expectedKind)
        where TOperation : class, IEditorOperation
    {
        if (!registrations.TryGetValue(id, out var registration))
            throw new KeyNotFoundException($"Editor operation '{id}' is not registered.");

        if (registration.Kind != expectedKind)
        {
            throw new InvalidOperationException(
                $"Editor operation '{id}' is a {registration.Kind}, not a {expectedKind}.");
        }

        var operation = registration.Factory();
        if (operation is not TOperation typedOperation)
        {
            throw new InvalidOperationException(
                $"Editor operation '{id}' does not implement {typeof(TOperation).Name}.");
        }

        return typedOperation;
    }

    private static Func<IEditorOperation> CreateFactory(Type type)
    {
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (constructor is null)
            throw new InvalidOperationException($"{type.FullName} must have a parameterless constructor.");

        return () =>
        {
            var instance = constructor.Invoke(null);
            if (instance is not IEditorOperation operation)
            {
                throw new InvalidOperationException(
                    $"{type.FullName} must implement {nameof(IEditorOperation)}.");
            }

            return operation;
        };
    }

    private static void ValidateAttribute(Type type, EditorOperationAttribute attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute.Id))
            throw new InvalidOperationException($"{type.FullName} has an empty editor operation id.");

        if (string.IsNullOrWhiteSpace(attribute.DisplayName))
            throw new InvalidOperationException($"{type.FullName} has an empty editor operation display name.");
    }

    private static OperationContract ResolveOperationContract(Type type)
    {
        var contracts = type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType)
            .Select(candidate => new
            {
                InterfaceType = candidate,
                Definition = candidate.GetGenericTypeDefinition(),
            })
            .Where(candidate =>
                candidate.Definition == typeof(IEditorCommand<,>)
                || candidate.Definition == typeof(IEditorQuery<,>)
                || candidate.Definition == typeof(IPreviewCommand<,>)
                || candidate.Definition == typeof(IPreviewQuery<,>))
            .ToArray();

        if (contracts.Length != 1)
        {
            throw new InvalidOperationException(
                $"{type.FullName} must implement exactly one editor operation contract.");
        }

        var contract = contracts[0];
        var kind = contract.Definition == typeof(IEditorCommand<,>)
            ? EditorOperationKind.Command
            : contract.Definition == typeof(IEditorQuery<,>)
                ? EditorOperationKind.Query
                : contract.Definition == typeof(IPreviewCommand<,>)
                    ? EditorOperationKind.PreviewCommand
                    : EditorOperationKind.PreviewQuery;

        var arguments = contract.InterfaceType.GetGenericArguments();
        return new OperationContract(kind, arguments[0], arguments[1]);
    }

    private sealed record OperationContract(
        EditorOperationKind Kind,
        Type OptionsType,
        Type ResultType);

    private sealed record EditorOperationRegistration(
        EditorOperationDescriptor Descriptor,
        Type OperationType,
        EditorOperationKind Kind,
        Type OptionsType,
        Type ResultType,
        Func<IEditorOperation> Factory);
}
