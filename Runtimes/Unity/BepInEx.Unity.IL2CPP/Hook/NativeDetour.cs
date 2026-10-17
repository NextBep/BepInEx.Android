using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace BepInEx.Unity.IL2CPP.Hook;

public unsafe class NativeDetour : IDetour
{
    public IntPtr Target { get; }
    public IntPtr Detour { get; }
    public bool SpecialReturnBuffer { get; }
    public IntPtr OriginalTrampoline { get; private set; }
    private readonly Delegate callback;
    private bool disposed;

    public NativeDetour(IntPtr target, Delegate detour, bool specialReturnBuffer = false)
    {
        if (target == IntPtr.Zero) throw new ArgumentException("Hook target is null", nameof(target));
        callback = detour ?? throw new ArgumentNullException(nameof(detour));
        Target = target;
        Detour = Marshal.GetFunctionPointerForDelegate(detour);
        SpecialReturnBuffer = specialReturnBuffer;
        Apply();
    }

    public void Apply()
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeDetour));
        if (OriginalTrampoline != IntPtr.Zero)
        {
            return;
        }
        OriginalTrampoline = NextInterop.hook(Target, Detour, SpecialReturnBuffer);
        if (OriginalTrampoline == IntPtr.Zero)
            throw new InvalidOperationException($"Native hook failed at 0x{Target.ToInt64():X} (return buffer: {SpecialReturnBuffer})");
        GC.KeepAlive(callback);
    }

    public void Dispose()
    {
        if (OriginalTrampoline == IntPtr.Zero)
        {
            return;
        }
        if (!NextInterop.unhook_checked(Target))
            throw new InvalidOperationException($"Native unhook failed at 0x{Target.ToInt64():X}");
        OriginalTrampoline = IntPtr.Zero;
        disposed = true;
        GC.KeepAlive(callback);
    }

    public T GenerateTrampoline<T>() where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(OriginalTrampoline);
    }
}
