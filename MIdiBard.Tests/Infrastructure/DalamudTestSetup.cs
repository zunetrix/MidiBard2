using System.Reflection;

using Dalamud.Plugin.Services;

using MidiBard;

using Moq;

namespace MidiBard.Tests.Infrastructure;

/// <summary>
/// Injects a no-op IPluginLog mock into DalamudApi before tests run.
/// DalamudApi.PluginLog has a private setter, so reflection is used to bypass it.
/// </summary>
public static class DalamudTestSetup
{
    private static bool _initialized;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;

            var mock = new Mock<IPluginLog>();
            typeof(DalamudApi)
                .GetProperty("PluginLog", BindingFlags.Public | BindingFlags.Static)!
                .SetValue(null, mock.Object);

            _initialized = true;
        }
    }
}
