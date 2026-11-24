using System;
using System.Runtime.InteropServices;

namespace NexaDock.Windows.Interop;

/// <summary>
/// Releases COM objects without ever crashing the bar. Shell and Core Audio objects can already be
/// gone (a window closed, a device unplugged) when we let go of them; .NET then throws from inside
/// ReleaseComObject, which is harmless but used to surface as an unhandled UI exception.
/// </summary>
internal static class ComRelease
{
    public static void Release(object? com, bool final = false)
    {
        if (com is null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(com))
            {
                if (final)
                {
                    Marshal.FinalReleaseComObject(com);
                }
                else
                {
                    Marshal.ReleaseComObject(com);
                }
            }
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentException or InvalidComObjectException)
        {
            // Already released or torn down: nothing left to free.
        }
    }
}
