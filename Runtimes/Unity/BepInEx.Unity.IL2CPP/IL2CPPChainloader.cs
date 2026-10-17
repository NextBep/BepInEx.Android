using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Preloader.Core;
using BepInEx.Preloader.Core.Logging;
using BepInEx.Unity.IL2CPP.Hook;
using BepInEx.Unity.IL2CPP.Logging;
using BepInEx.Unity.IL2CPP.Utils;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime.InteropTypes;
using MonoMod.Utils;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BepInEx.Unity.IL2CPP;

public class IL2CPPChainloader : BaseChainloader<BasePlugin>
{
    private static RuntimeInvokeDetourDelegate originalInvoke;

    private static readonly ConfigEntry<bool> ConfigUnityLogging = ConfigFile.CoreConfig.Bind(
     "Logging", "UnityLogListening",
     true,
     "Enables showing unity log messages in the BepInEx logging system.");

    private static readonly ConfigEntry<bool> ConfigDiskWriteUnityLog = ConfigFile.CoreConfig.Bind(
     "Logging.Disk", "WriteUnityLog",
     false,
     "Include unity log messages in log file output.");


    private static NativeDetour RuntimeInvokeDetour { get; set; }

    public static IL2CPPChainloader Instance { get; set; }

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <typeparam name="T">Type of the component to add.</typeparam>
    public static T AddUnityComponent<T>() where T : Il2CppObjectBase => AddUnityComponent(typeof(T)).Cast<T>();

    /// <summary>
    ///     Register and add a Unity Component (for example MonoBehaviour) into BepInEx global manager.
    ///     Automatically registers the type with Il2Cpp type system if it isn't initialised already.
    /// </summary>
    /// <param name="t">Type of the component to add</param>
    public static Il2CppObjectBase AddUnityComponent(Type t) => Il2CppUtils.AddComponent(t);

    /// <summary>
    ///     Occurs after a plugin is instantiated and just before <see cref="BasePlugin.Load"/> is called.
    /// </summary>
    public event Action<PluginInfo, Assembly, BasePlugin> PluginLoad;

    public override void Initialize(string gameExePath = null)
    {
        base.Initialize(gameExePath);
        Instance = this;

        string gameLibraryName;
        if (PlatformDetection.OS is OSKind.Windows or OSKind.OSX)
        {
            gameLibraryName = "GameAssembly";
        }
        else if (PlatformDetection.OS is OSKind.Linux or OSKind.Android)
        {
            gameLibraryName = "libil2cpp.so";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform!");
        }

        if (!NativeLibrary.TryLoad(gameLibraryName, typeof(IL2CPPChainloader).Assembly, null, out var il2CppHandle))
        {
            Logger.Log(LogLevel.Fatal,
                       "Could not locate Il2Cpp game assembly (GameAssembly.dll, UserAssembly.dll or libil2cpp.so). The game might be obfuscated or use a yet unsupported build of Unity.");
            return;
        }

        var runtimeInvokePtr = NativeLibrary.GetExport(il2CppHandle, NextInterop.GetIl2CppApi("il2cpp_runtime_invoke"));
        runtimeInvokePtr = XrefScannerLowLevel.JumpTargets(runtimeInvokePtr).First();

        PreloaderLogger.Log.Log(LogLevel.Debug, $"Runtime invoke pointer: 0x{runtimeInvokePtr.ToInt64():X}");
        RuntimeInvokeDetourDelegate invokeMethodDetour = OnInvokeMethod;

        RuntimeInvokeDetour = new NativeDetour(runtimeInvokePtr, invokeMethodDetour);
        originalInvoke = RuntimeInvokeDetour.GenerateTrampoline<RuntimeInvokeDetourDelegate>();

        PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke patched");
        NextInterop.set_loader_message("Initialized Chainloader...");
    }

    private static IntPtr OnInvokeMethod(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc)
    {
        var methodName = Marshal.PtrToStringAnsi(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method));

        var unhook = false;

        if (methodName == "Internal_ActiveSceneChanged")
        {
            try
            {
                NextInterop.set_loader_stage(NextInterop.LoaderStage.Chainloader);
                NextInterop.set_loader_message("Running Chainloader...");

                if (ConfigUnityLogging.Value)
                    try
                    {
                        Logger.Sources.Add(new IL2CPPUnityLogSource());

                        Application.CallLogCallback("Test call after applying unity logging hook", "", LogType.Assert,
                                                    true);
                    }
                    catch (Exception ex)
                    {
                        // Enabling Unity log forwarding requires the UnityEngine.Application::SetLogCallbackDefined
                        // ICall. Game builds stripped of it (or builds that never use Unity's log callback) cannot
                        // enable it, so degrade gracefully instead of failing the whole chainloader.
                        Logger.Log(LogLevel.Warning,
                                   "Unable to enable Unity log listening; Unity log messages will not be forwarded to BepInEx logs for this game");
                        Logger.Log(LogLevel.Debug, ex);
                    }

                unhook = true;

                Il2CppInteropManager.PreloadInteropAssemblies();

                Instance.Execute();
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Fatal, "Unable to execute IL2CPP chainloader");
                Logger.Log(LogLevel.Error, ex);
            }
            finally
            {
                NextInterop.set_loader_stage(NextInterop.LoaderStage.Finished);
            }
        }

        var result = originalInvoke(method, obj, parameters, exc);

        if (unhook)
        {
            RuntimeInvokeDetour.Dispose();

            PreloaderLogger.Log.Log(LogLevel.Debug, "Runtime invoke unpatched");
        }

        return result;
    }

    protected override IList<PluginInfo> DiscoverPlugins()
    {
        var plugins = DiscoverPluginsFrom(Paths.PluginPath).ToList();
        foreach (var pluginPath in NextCoreEntrypoint.AuxiliaryPluginFolders)
        {
            Logger.Log(LogLevel.Info, "Loading auxiliary plugin path " + pluginPath);
            plugins.AddRange(DiscoverPluginsFrom(pluginPath));
        }

        return plugins;
    }

    protected override void InitializeLoggers()
    {
        base.InitializeLoggers();

        if (!ConfigDiskWriteUnityLog.Value) DiskLogListener.BlacklistedSources.Add("Unity");

        Logger.Sources.Add(new IL2CPPLogSource());
    }

    public override BasePlugin LoadPlugin(PluginInfo pluginInfo, Assembly pluginAssembly)
    {
        NextInterop.set_loader_message($"Loading {pluginInfo.Metadata.Name}");
        var type = pluginAssembly.GetType(pluginInfo.TypeName);

        var pluginInstance = (BasePlugin) Activator.CreateInstance(type);

        PluginLoad?.Invoke(pluginInfo, pluginAssembly, pluginInstance);
        pluginInstance.Load();

        return pluginInstance;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RuntimeInvokeDetourDelegate(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc);
}
