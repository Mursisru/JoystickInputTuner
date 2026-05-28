using System.Reflection;
using System.Runtime.InteropServices;

namespace JoystickInputTuner.Input.VJoy;

internal static class VJoyNative
{
    private const string DllName = "vJoyInterface.dll";
    private static readonly string InstalledDllPath = @"C:\Program Files\vJoy\x64\vJoyInterface.dll";

    static VJoyNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(VJoyNative).Assembly, ResolveDll);
    }

    private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        var localPath = Path.Combine(AppContext.BaseDirectory, DllName);
        if (File.Exists(localPath) && NativeLibrary.TryLoad(localPath, out var localHandle))
            return localHandle;

        if (File.Exists(InstalledDllPath) && NativeLibrary.TryLoad(InstalledDllPath, out var installedHandle))
            return installedHandle;

        return IntPtr.Zero;
    }

    private const uint HidUsageX = 0x30;
    private const uint HidUsageY = 0x31;
    private const uint HidUsageZ = 0x32;
    private const uint HidUsageRx = 0x33;
    private const uint HidUsageRy = 0x34;
    private const uint HidUsageRz = 0x35;
    private const uint HidUsageSl0 = 0x36;
    private const uint HidUsageSl1 = 0x37;

    private const int AxisMax = 0x8000;
    private const int AxisCenter = 0x4000;

    private const uint VjdStatOwn = 0;
    private const uint VjdStatFree = 1;
    private const uint VjdStatBusy = 2;
    private const uint VjdStatMiss = 3;

    public static bool IsDriverAvailable()
    {
        try
        {
            return vJoyEnabled();
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool TryAcquire(uint deviceId, out string? error)
    {
        error = null;
        if (!IsDriverAvailable())
        {
            error = "vJoy driver is not installed or vJoyInterface.dll is missing.";
            return false;
        }

        try
        {
            if (!IsVjdExists(deviceId))
            {
                error = $"vJoy device #{deviceId} is not configured. Open Configure vJoy, enable device #{deviceId}, then Apply.";
                return false;
            }

            var status = GetVjdStatus(deviceId);
            switch (status)
            {
                case VjdStatOwn:
                    return true;
                case VjdStatFree:
                    if (AcquireVjd(deviceId))
                        return true;
                    error = $"Cannot acquire vJoy device #{deviceId}.";
                    return false;
                case VjdStatBusy:
                    if (TryRecoverBusyDevice(deviceId))
                        return true;

                    error =
                        $"vJoy device #{deviceId} is used by another application. " +
                        "Close the game/simulator and other vJoy feeders, stop other JoystickInputTuner sessions, " +
                        "or pick another vJoy device id in settings.";
                    return false;
                case VjdStatMiss:
                    error = $"vJoy device #{deviceId} is not available (driver or device missing).";
                    return false;
                default:
                    error = $"vJoy device #{deviceId} status is unknown.";
                    return false;
            }
        }
        catch (DllNotFoundException)
        {
            error = "vJoyInterface.dll not found next to the app. Rebuild or copy it from Program Files\\vJoy\\x64.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Relinquish(uint deviceId)
    {
        try
        {
            RelinquishVJD(deviceId);
        }
        catch (DllNotFoundException)
        {
        }
    }

    /// <summary>Releases device if this process still owns it (e.g. after crash/restart).</summary>
    public static void TryReleaseStale(uint deviceId)
    {
        if (!IsDriverAvailable())
            return;

        try
        {
            if (!IsVjdExists(deviceId))
                return;

            if (GetVjdStatus(deviceId) == VjdStatOwn)
                Relinquish(deviceId);
        }
        catch (DllNotFoundException)
        {
        }
    }

    private static bool TryRecoverBusyDevice(uint deviceId)
    {
        try
        {
            Relinquish(deviceId);
            Thread.Sleep(60);
            var status = GetVjdStatus(deviceId);
            if (status == VjdStatOwn)
                return true;

            if (status != VjdStatFree)
                return false;

            return AcquireVjd(deviceId);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    public static bool SetAxisNormalized(uint deviceId, string axisId, double normalized)
    {
        if (!TryMapAxis(axisId, out var usage))
            return false;

        var clamped = Math.Clamp(normalized, -1.0, 1.0);
        var value = (int)Math.Round((clamped + 1.0) * 0.5 * AxisMax);
        value = Math.Clamp(value, 0, AxisMax);
        return SetAxis(value, deviceId, usage);
    }

    public static void CenterAllKnownAxes(uint deviceId)
    {
        SetAxis(AxisCenter, deviceId, HidUsageX);
        SetAxis(AxisCenter, deviceId, HidUsageY);
        SetAxis(AxisCenter, deviceId, HidUsageZ);
        SetAxis(AxisCenter, deviceId, HidUsageRx);
        SetAxis(AxisCenter, deviceId, HidUsageRy);
        SetAxis(AxisCenter, deviceId, HidUsageRz);
        SetAxis(AxisCenter, deviceId, HidUsageSl0);
        SetAxis(AxisCenter, deviceId, HidUsageSl1);
    }

    private static bool TryMapAxis(string axisId, out uint usage)
    {
        usage = axisId.ToUpperInvariant() switch
        {
            "X" => HidUsageX,
            "Y" => HidUsageY,
            "Z" => HidUsageZ,
            "RX" => HidUsageRx,
            "RY" => HidUsageRy,
            "RZ" => HidUsageRz,
            "SL0" => HidUsageSl0,
            "SL1" => HidUsageSl1,
            _ => 0
        };

        return usage != 0;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool vJoyEnabled();

    [DllImport(DllName, EntryPoint = "GetVJDStatus", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetVjdStatus(uint rId);

    [DllImport(DllName, EntryPoint = "isVJDExists", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool IsVjdExists(uint rId);

    [DllImport(DllName, EntryPoint = "AcquireVJD", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AcquireVjd(uint rId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void RelinquishVJD(uint rId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern bool SetAxis(int value, uint rId, uint axis);
}
