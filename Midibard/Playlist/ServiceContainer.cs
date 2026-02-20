using System;
using System.Collections.Generic;

namespace MidiBard.Playlist;

/// <summary>
/// Simple service container for dependency injection
/// </summary>
public static class ServiceContainer
{
    private static readonly Dictionary<Type, object> _services = new();
    private static readonly Dictionary<Type, Func<object>> _factories = new();
    private static bool _isLocked = false;

    /// <summary>
    /// Register a singleton service
    /// </summary>
    public static void Register<T>(T instance) where T : class
    {
        if (_isLocked)
            throw new InvalidOperationException("Cannot register services after initialization is complete");

        _services[typeof(T)] = instance;
    }

    /// <summary>
    /// Register a factory for lazy instantiation
    /// </summary>
    public static void RegisterFactory<T>(Func<T> factory) where T : class
    {
        if (_isLocked)
            throw new InvalidOperationException("Cannot register services after initialization is complete");

        _factories[typeof(T)] = () => factory();
    }

    /// <summary>
    /// Get a service (throws if not found)
    /// </summary>
    public static T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
        {
            return (T)service;
        }

        if (_factories.TryGetValue(typeof(T), out var factory))
        {
            var instance = (T)factory();
            _services[typeof(T)] = instance; // Cache the instance
            return instance;
        }

        throw new InvalidOperationException($"Service {typeof(T).Name} not registered");
    }

    /// <summary>
    /// Try to get a service (returns null if not found)
    /// </summary>
    public static T? TryGet<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
        {
            return (T)service;
        }

        if (_factories.TryGetValue(typeof(T), out var factory))
        {
            var instance = (T)factory();
            _services[typeof(T)] = instance;
            return instance;
        }

        return null;
    }

    /// <summary>
    /// Check if a service is registered
    /// </summary>
    public static bool IsRegistered<T>()
    {
        return _services.ContainsKey(typeof(T)) || _factories.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Lock the container - no more registrations allowed
    /// </summary>
    public static void Lock()
    {
        _isLocked = true;
    }

    /// <summary>
    /// Clear all services (mainly for testing)
    /// </summary>
    public static void Clear()
    {
        _services.Clear();
        _factories.Clear();
        _isLocked = false;
    }
}
