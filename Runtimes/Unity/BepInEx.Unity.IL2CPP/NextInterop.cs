using System;
using System.Runtime.InteropServices;

namespace BepInEx.Unity.IL2CPP;

internal static partial class NextInterop
{
    private const string LIBRARY_NAME = "fusion";

    public static bool LowMemoryMode => get_low_memory_mode() == 1;

    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial nint get_il2cpp_api(string name);

    public static string GetIl2CppApi(string name)
    {
        var ptr = get_il2cpp_api(name);
        if (ptr == nint.Zero) return name;

        return Marshal.PtrToStringUTF8(ptr) ?? name;
    }

    [LibraryImport(LIBRARY_NAME)]
    public static unsafe partial void set_loader_stage(LoaderStage loadingStage);

    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void set_loader_message(string message);

    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void write_log_level(int logLevel, string message);

    [LibraryImport(LIBRARY_NAME)]
    // ReSharper disable once InconsistentNaming
    public static unsafe partial IntPtr hook(IntPtr target, IntPtr detour,
                                             [MarshalAs(UnmanagedType.I1)] bool specialReturnBuffer);

    [LibraryImport(LIBRARY_NAME)]
    public static unsafe partial void unhook(IntPtr target);

    [LibraryImport(LIBRARY_NAME)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static unsafe partial bool unhook_checked(IntPtr target);

    [LibraryImport(LIBRARY_NAME)]
    public static unsafe partial byte get_low_memory_mode();

    public enum LoaderStage : byte
    {
        Preloader,
        Chainloader,
        Finished
    }
}
