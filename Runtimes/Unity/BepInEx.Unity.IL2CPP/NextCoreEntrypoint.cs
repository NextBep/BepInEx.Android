using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx.Preloader.Core;
using BepInEx.Unity.IL2CPP.Utils;
using MonoMod.Utils;

namespace BepInEx.Unity.IL2CPP;

internal static class NextCoreEntrypoint
{
    public static readonly List<string> AuxiliaryPluginFolders = [];

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct AuxPluginFolderList
    {
        public int Count;
        public nint* Folders;
    }

    /// <summary>
    ///     The main entrypoint of BepInEx, called from NextCore.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Start", CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static unsafe void Start(AuxPluginFolderList* folderList)
    {
        // We set it to the current directory first as a fallback, but try to use the same location as the .exe file.
        var silentExceptionLog = Environment.GetEnvironmentVariable("BEPINEX_PRELOADER_LOG") ??
                                 $"preloader_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.WriteLine(args.ExceptionObject.ToString());
        };

        try
        {
            EnvVars.LoadVars();

            silentExceptionLog =
                Path.Combine(Path.GetDirectoryName(EnvVars.NEXT_APP_DATA_DIR)!, silentExceptionLog);

            if (folderList != null && folderList->Folders != null)
            {
                for (var i = 0; i < folderList->Count; i++)
                {
                    var cString = folderList->Folders[i];
                    if (cString == IntPtr.Zero) continue;

                    var str = Marshal.PtrToStringAnsi(cString);
                    if (string.IsNullOrEmpty(str)) continue;

                    AuxiliaryPluginFolders.Add(str);
                }
            }

            UnityPreloaderRunner.PreloaderMain();
        }
        catch (Exception ex)
        {
            File.WriteAllText(silentExceptionLog, ex.ToString());

            try
            {
                if (PlatformDetection.OS is OSKind.Windows)
                {
                    MessageBox.Show("Failed to start BepInEx", "BepInEx");
                }
                else if (NotifySend.IsSupported)
                {
                    NotifySend.Send("Failed to start BepInEx", "Check logs for details");
                }
                else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BEPINEX_FAIL_FAST")))
                {
                    // Don't exit the game if we have no way of signaling to the user that a crash happened
                    return;
                }
            }
            catch (Exception)
            {
                // ignored
            }

            Environment.Exit(1);
        }
    }
}
