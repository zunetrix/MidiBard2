using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MidiBard.Util;

internal static class DryWetMidiNativeResolver
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        var alc = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
        if (alc == null)
            return;

        alc.ResolvingUnmanagedDll += ResolveUnmanaged;
        _registered = true;

        DalamudApi.PluginLog.Debug("[DryWetMidi] Native resolver registered");
    }

    public static void Unregister()
    {
        if (!_registered)
            return;

        var alc = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
        if (alc == null)
            return;

        alc.ResolvingUnmanagedDll -= ResolveUnmanaged;
        _registered = false;
    }

    private static nint ResolveUnmanaged(Assembly assembly, string libraryName)
    {
        if (!libraryName.Contains("DryWetMidi", StringComparison.OrdinalIgnoreCase))
            return nint.Zero;

        try
        {
            var pluginDir = DalamudApi.PluginInterface.AssemblyLocation.Directory?.FullName;
            if (string.IsNullOrEmpty(pluginDir))
                return nint.Zero;

            var fileName = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? libraryName
                : libraryName + ".dll";

            var fullPath = Path.Combine(pluginDir, fileName);

            DalamudApi.PluginLog.Debug($"[DryWetMidi] Trying load: {fullPath}");

            if (File.Exists(fullPath) &&
                NativeLibrary.TryLoad(fullPath, out var handle))
            {
                DalamudApi.PluginLog.Debug($"[DryWetMidi] Loaded native: {fileName}");
                return handle;
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[DryWetMidi] Failed to resolve native DLL");
        }

        return nint.Zero;
    }
}
