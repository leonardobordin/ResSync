using System.Runtime.InteropServices;

namespace ResolutionManager.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct DEVMODE
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmDeviceName;
    public short dmSpecVersion;
    public short dmDriverVersion;
    public short dmSize;
    public short dmDriverExtra;
    public int dmFields;
    public int dmPositionX;
    public int dmPositionY;
    public int dmDisplayOrientation;
    public int dmDisplayFixedOutput;
    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string dmFormName;
    public short dmLogPixels;
    public short dmBitsPerPel;
    public int dmPelsWidth;
    public int dmPelsHeight;
    public int dmDisplayFlags;
    public int dmDisplayFrequency;
    public int dmICMMethod;
    public int dmICMIntent;
    public int dmMediaType;
    public int dmDitherType;
    public int dmReserved1;
    public int dmReserved2;
    public int dmPanningWidth;
    public int dmPanningHeight;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct DISPLAY_DEVICE
{
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;
    public int StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;
}

/// <summary>
/// Three-channel gamma ramp, each channel 256 WORD values.
/// Used by GetDeviceGammaRamp / SetDeviceGammaRamp.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RAMP
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Red;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Green;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public ushort[] Blue;
}

public static class NativeMethods
{
    // Display settings flags
    public const int ENUM_CURRENT_SETTINGS  = -1;
    public const int CDS_UPDATEREGISTRY     = 0x00000001;
    public const int CDS_TEST               = 0x00000002;
    public const int DISP_CHANGE_SUCCESSFUL = 0;
    public const int DISP_CHANGE_RESTART    = 1;
    public const int DISP_CHANGE_FAILED     = -1;
    public const int DISP_CHANGE_BADMODE    = -2;

    // DISPLAY_DEVICE.StateFlags
    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    public const int DISPLAY_DEVICE_PRIMARY_DEVICE       = 0x00000004;

    // dmFields flags for display settings
    public const int DM_BITSPERPEL      = 0x00040000;
    public const int DM_PELSWIDTH       = 0x00080000;
    public const int DM_PELSHEIGHT      = 0x00100000;
    public const int DM_DISPLAYFREQUENCY = 0x00400000;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode,
        IntPtr hwnd, int flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    // ── GDI DC helpers (for gamma ramp vibrance) ──────────────────────────────

    [DllImport("gdi32.dll")]
    public static extern bool GetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

    [DllImport("gdi32.dll")]
    public static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>Creates a DC for the given device name (e.g. "\\.\DISPLAY1").</summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice,
        string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    // ── Keyboard input (for GPU driver reset shortcut) ─────────────────────

    public const byte VK_LWIN    = 0x5B;
    public const byte VK_CONTROL = 0x11;
    public const byte VK_SHIFT   = 0x10;
    public const byte VK_B       = 0x42;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
