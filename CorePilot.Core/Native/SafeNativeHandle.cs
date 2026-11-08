using System.Runtime.InteropServices;

namespace CorePilot.Core.Native;

/// <summary>Owns a Win32 kernel handle and closes it with CloseHandle.</summary>
internal sealed class SafeNativeHandle : SafeHandle
{
    public SafeNativeHandle()
        : base(IntPtr.Zero, ownsHandle: true)
    {
    }

    public SafeNativeHandle(IntPtr handle)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
