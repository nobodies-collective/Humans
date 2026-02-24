using System.Runtime.InteropServices;

namespace Humans.Web.Helpers;

/// <summary>
/// Registers a DllImportResolver so LibHeifSharp can find the native library
/// shipped by PhotoSauce.NativeCodecs.Libheif (named "heif.dll" on Windows
/// instead of "libheif.dll"). On Linux the .so is named correctly and needs no resolver.
/// Safe to call multiple times — only the first call registers.
/// </summary>
internal static class LibHeifResolver
{
    private static int _registered;

    internal static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        NativeLibrary.SetDllImportResolver(
            typeof(LibHeifSharp.HeifContext).Assembly,
            (name, assembly, searchPath) =>
            {
                if (string.Equals(name, "libheif", StringComparison.Ordinal)
                    && OperatingSystem.IsWindows())
                {
                    if (NativeLibrary.TryLoad("heif", assembly, searchPath, out var handle))
                        return handle;
                }
                return IntPtr.Zero;
            });
    }
}
