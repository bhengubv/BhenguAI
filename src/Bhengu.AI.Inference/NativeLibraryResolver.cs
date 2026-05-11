// NativeLibraryResolver.cs
//
// Registers a custom DllImportResolver that searches for llama.cpp native
// binaries under a `runtimes/{RID}/native/` directory relative to the
// assembly location. This is the standard NuGet native library layout.
//
// Placement guide (ship these alongside Bhengu.AI.Inference.dll):
//   Windows x64 : runtimes/win-x64/native/llama.dll
//   Linux x64   : runtimes/linux-x64/native/libllama.so
//   macOS arm64 : runtimes/osx-arm64/native/libllama.dylib
//   Android arm64: runtimes/android-arm64/native/libllama.so
//   iOS arm64   : runtimes/ios-arm64/native/libllama.dylib

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Bhengu.AI.Inference;

public static class NativeLibraryResolver
{
    private static bool _registered;
    private static readonly object _lock = new();

    /// <summary>
    /// Optional override directory injected by the host (e.g. Android
    /// <c>nativeLibraryDir</c>, or a custom dev-machine path).
    /// Set this before calling <see cref="EnsureRegistered"/>.
    /// </summary>
    public static string? OverrideDirectory { get; set; }

    /// <summary>
    /// Register the resolver. Safe to call multiple times; registration
    /// only happens once per process.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (_lock)
        {
            if (_registered) return;
            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(), Resolve);
            _registered = true;
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var nativeFileName = GetNativeFileName(libraryName);
        if (nativeFileName is null) return nint.Zero;

        // 1. Host-injected override directory (Android nativeLibraryDir, custom path).
        if (!string.IsNullOrWhiteSpace(OverrideDirectory))
        {
            var overrideCandidate = Path.Combine(OverrideDirectory, nativeFileName);
            if (File.Exists(overrideCandidate) &&
                NativeLibrary.TryLoad(overrideCandidate, out var overrideHandle))
                return overrideHandle;
        }

        // 2. Standard runtimes/{RID}/native/ layout (NuGet / desktop deployment).
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (!string.IsNullOrWhiteSpace(rid))
        {
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            var candidate = Path.Combine(assemblyDir, "runtimes", rid, "native", nativeFileName);
            if (File.Exists(candidate) &&
                NativeLibrary.TryLoad(candidate, out var handle))
                return handle;
        }

        // 3. Same directory as the assembly (flat deployment).
        var assemblyFlat = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
        var flat = Path.Combine(assemblyFlat, nativeFileName);
        if (File.Exists(flat) &&
            NativeLibrary.TryLoad(flat, out var flatHandle))
            return flatHandle;

        // 4. AppContext.BaseDirectory (Windows Service / self-contained publish).
        var baseDir = Path.Combine(AppContext.BaseDirectory, nativeFileName);
        if (File.Exists(baseDir) &&
            NativeLibrary.TryLoad(baseDir, out var baseDirHandle))
            return baseDirHandle;

        return nint.Zero; // Fall back to default OS resolution.
    }

    private static string? GetNativeFileName(string libraryName)
    {
        // Normalise: strip leading "lib" and any extension.
        var name = Path.GetFileNameWithoutExtension(libraryName)
            .TrimStart('l', 'i', 'b'); // correct for llama / llava

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{name}.dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"lib{name}.dylib";

        // Linux + Android
        return $"lib{name}.so";
    }
}
