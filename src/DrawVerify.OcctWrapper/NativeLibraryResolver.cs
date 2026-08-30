using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DrawVerify.OcctWrapper;

/// <summary>Resolves the DrawVerify native bridge from the deployed application layout.</summary>
internal static class NativeLibraryResolver
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:ModuleInitializers should not be used in libraries", Justification = "The resolver must be installed before generated SWIG P/Invoke calls.")]
    [ModuleInitializer]
    internal static void Install() => NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "DrawVerifyOcctNative", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "DrawVerifyOcctNative.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libDrawVerifyOcctNative.dylib"
            : "libDrawVerifyOcctNative.so";
        var runtimeId = RuntimeInformation.RuntimeIdentifier;
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "native"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeId, "native"),
            Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory
        };

        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            var candidate = Path.Combine(root, fileName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
