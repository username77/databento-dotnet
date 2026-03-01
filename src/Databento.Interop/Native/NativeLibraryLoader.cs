using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Databento.Interop.Native;

/// <summary>
/// Handles loading of native libraries with proper path resolution
/// </summary>
internal static class NativeLibraryLoader
{
    private static readonly object Lock = new();
    private static bool _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_initialized)
            return;

        lock (Lock)
        {
            if (_initialized)
                return;

            // Pre-load all dependency DLLs before databento_native is loaded
            PreloadDependencies();

            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, DllImportResolver);
            _initialized = true;
        }
    }

    private static void PreloadDependencies()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        // Windows dependency order: VC++ runtime first, then libraries
        var dependencies = new[]
        {
            "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll",
            "zstd.dll", "legacy.dll", "libcrypto-3-x64.dll", "libssl-3-x64.dll"
        };
        var locations = GetSearchLocations().ToList();

        foreach (var dep in dependencies)
        {
            bool loaded = false;
            foreach (var location in locations)
            {
                var dllPath = Path.Combine(location, dep);
                if (File.Exists(dllPath))
                {
                    try
                    {
                        if (NativeLibrary.TryLoad(dllPath, out var handle))
                        {
                            loaded = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Databento] Failed to preload dependency '{dep}' from '{dllPath}': {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (!loaded)
            {
                // VC++ runtime DLLs may already be loaded system-wide; only warn for our bundled libs
                if (!dep.StartsWith("msvcp", StringComparison.OrdinalIgnoreCase) &&
                    !dep.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Databento] Could not preload dependency '{dep}' from any search location.");
                }
            }
        }
    }

    private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only handle databento_native, let other DLLs load normally
        if (libraryName != "databento_native")
        {
            return IntPtr.Zero;
        }

        // Try to load from multiple locations
        var locations = GetSearchLocations().ToList();
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Failed to load native library '{libraryName}'.");
        diagnostics.AppendLine();
        diagnostics.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        diagnostics.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        diagnostics.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        diagnostics.AppendLine($"RID: {GetRuntimeIdentifier() ?? "(unknown)"}");
        diagnostics.AppendLine();
        diagnostics.AppendLine("Search paths:");

        var platformName = GetPlatformLibraryName(libraryName);

        foreach (var location in locations)
        {
            var dllPath = Path.Combine(location, platformName);
            var exists = File.Exists(dllPath);

            if (exists)
            {
                try
                {
                    if (NativeLibrary.TryLoad(dllPath, out var handle))
                    {
                        return handle;
                    }
                    diagnostics.AppendLine($"  [EXISTS BUT FAILED TO LOAD] {dllPath}");
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"  [LOAD ERROR: {ex.GetType().Name}] {dllPath}");
                }
            }
            else
            {
                diagnostics.AppendLine($"  [NOT FOUND] {dllPath}");
            }
        }

        // Check dependency status
        diagnostics.AppendLine();
        diagnostics.AppendLine("Dependency status:");
        var depCheckList = new[]
        {
            "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll",
            "zstd.dll", "legacy.dll", "libcrypto-3-x64.dll", "libssl-3-x64.dll"
        };
        foreach (var dep in depCheckList)
        {
            bool found = false;
            foreach (var location in locations)
            {
                if (File.Exists(Path.Combine(location, dep)))
                {
                    diagnostics.AppendLine($"  [FOUND] {dep} in {location}");
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                diagnostics.AppendLine($"  [NOT FOUND] {dep}");
            }
        }

        // Check for debug-build DLL dependencies that won't work on end-user machines
        var debugDlls = new[] { "VCRUNTIME140D.dll", "ucrtbased.dll", "MSVCP140D.dll" };
        bool hasDebugDep = false;
        foreach (var debugDll in debugDlls)
        {
            if (NativeLibrary.TryLoad(debugDll, out _))
                continue;
            // Debug runtime not available - this is expected on end-user machines
            hasDebugDep = true;
        }
        if (hasDebugDep)
        {
            diagnostics.AppendLine();
            diagnostics.AppendLine("NOTE: Some native DLLs may have been built with Debug configuration");
            diagnostics.AppendLine("and require VCRUNTIME140D.dll / ucrtbased.dll (debug C++ runtime).");
            diagnostics.AppendLine("These are only available with Visual Studio installed.");
            diagnostics.AppendLine("The native DLLs must be rebuilt with Release configuration.");
        }

        throw new DllNotFoundException(diagnostics.ToString());
    }

    private static string GetPlatformLibraryName(string libraryName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{libraryName}.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"lib{libraryName}.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"lib{libraryName}.dylib";

        return libraryName;
    }

    private static IEnumerable<string> GetSearchLocations()
    {
        var assemblyLocation = Path.GetDirectoryName(typeof(NativeMethods).Assembly.Location);
        var appBaseDirectory = AppContext.BaseDirectory;

        // 1. Application base directory (most common for published apps)
        yield return appBaseDirectory;

        // 2. Assembly location (where Databento.Interop.dll is)
        if (assemblyLocation != null && assemblyLocation != appBaseDirectory)
            yield return assemblyLocation;

        // 3. runtimes/{rid}/native folder in app base directory
        var rid = GetRuntimeIdentifier();
        if (rid != null)
        {
            yield return Path.Combine(appBaseDirectory, "runtimes", rid, "native");

            if (assemblyLocation != null)
                yield return Path.Combine(assemblyLocation, "runtimes", rid, "native");
        }

        // 4. Current working directory
        yield return Directory.GetCurrentDirectory();
    }

    private static string? GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => null
            };
        }

        return null;
    }
}
