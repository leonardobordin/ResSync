using System.Runtime.InteropServices;

namespace ResolutionManager.Services;

/// <summary>
/// Controls NVIDIA Digital Vibrance via nvapi64.dll.
/// Uses the exact same function IDs and calling convention as vibranceGUI:
///   LoadLibrary → GetProcAddress("nvapi_QueryInterface") → non-Ex DVC functions.
/// </summary>
internal static class NvApiService
{
    // ── NvAPI function IDs (same as vibranceGUI) ──────────────────────────────
    private const uint FID_Initialize                       = 0x0150E828;
    private const uint FID_EnumNvidiaDisplayHandle          = 0x9ABDD40D;
    private const uint FID_GetAssociatedNvidiaDisplayHandle = 0x35C29134;
    private const uint FID_GetAssociatedNvidiaDisplayName   = 0x22A78B05;
    private const uint FID_GetDVCInfo                       = 0x4085DE45;   // non-Ex
    private const uint FID_SetDVCLevel                      = 0x172409B4;   // non-Ex

    private const int NVAPI_OK = 0;

    // NV_DISPLAY_DVC_INFO v1 — 16 bytes; MAKE_NVAPI_VERSION(NV_DISPLAY_DVC_INFO,1)
    private const uint NV_DVC_INFO_VER = 16u | (1u << 16);  // = 65552

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct NV_DISPLAY_DVC_INFO
    {
        public uint version;
        public int  currentLevel;
        public int  minLevel;
        public int  maxLevel;
    }

    // ── Delegates ─────────────────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Del_QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_Initialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_EnumDisplay(uint index, out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_GetAssocHandle(
        [MarshalAs(UnmanagedType.LPStr)] string displayName, out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_GetDisplayName(IntPtr handle, IntPtr nameBuf);

    // NvAPI_GetDVCInfo(NvDisplayHandle, NvU32 outputId, NV_DISPLAY_DVC_INFO*)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_GetDVCInfo(IntPtr hDisp, uint outputId, ref NV_DISPLAY_DVC_INFO info);

    // NvAPI_SetDVCLevel(NvDisplayHandle, NvU32 outputId, NvS32 level)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Del_SetDVCLevel(IntPtr hDisp, uint outputId, int level);

    // ── kernel32 P/Invoke ─────────────────────────────────────────────────────
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // ── State ─────────────────────────────────────────────────────────────────
    private static bool              _initialized;
    private static bool              _available;
    private static string            _lastError = "Not initialized";

    private static Del_QueryInterface? _queryInterface;
    private static Del_EnumDisplay?    _enumDisplay;
    private static Del_GetAssocHandle? _getAssocHandle;
    private static Del_GetDisplayName? _getDisplayName;
    private static Del_GetDVCInfo?     _getDVC;
    private static Del_SetDVCLevel?    _setDVC;

    // ── Public API ────────────────────────────────────────────────────────────

    public static bool IsAvailable
    {
        get { if (!_initialized) { _available = Load(); _initialized = true; } return _available; }
    }

    public static string LastError => _lastError;

    public static bool SetVibrance(string? deviceName, int percent)
    {
        if (!IsAvailable) return false;
        percent = Math.Clamp(percent, 0, 100);

        IntPtr hDisp = GetDisplayHandle(deviceName);
        if (hDisp == IntPtr.Zero) { _lastError = "SetVibrance: display handle not found"; return false; }

        var info = new NV_DISPLAY_DVC_INFO { version = NV_DVC_INFO_VER };
        int rc = _getDVC!(hDisp, 0u, ref info);
        if (rc != NVAPI_OK) { _lastError = $"GetDVCInfo returned {rc}"; return false; }

        int targetLevel = info.minLevel + (int)Math.Round((info.maxLevel - info.minLevel) * (percent / 100.0));
        rc = _setDVC!(hDisp, 0u, targetLevel);
        if (rc != NVAPI_OK) { _lastError = $"SetDVCLevel returned {rc} (level={targetLevel})"; return false; }

        _lastError = $"OK level={targetLevel} (range {info.minLevel}..{info.maxLevel})";
        return true;
    }

    public static bool RestoreToLevel(string? deviceName, int rawLevel)
    {
        if (!IsAvailable) return false;
        IntPtr hDisp = GetDisplayHandle(deviceName);
        if (hDisp == IntPtr.Zero) return false;
        var info = new NV_DISPLAY_DVC_INFO { version = NV_DVC_INFO_VER };
        if (_getDVC!(hDisp, 0u, ref info) != NVAPI_OK) return false;
        int clamped = Math.Clamp(rawLevel, info.minLevel, info.maxLevel);
        return _setDVC!(hDisp, 0u, clamped) == NVAPI_OK;
    }

    public static int GetCurrentLevel(string? deviceName)
    {
        if (!IsAvailable) return int.MinValue;
        IntPtr hDisp = GetDisplayHandle(deviceName);
        if (hDisp == IntPtr.Zero) return int.MinValue;
        var info = new NV_DISPLAY_DVC_INFO { version = NV_DVC_INFO_VER };
        return _getDVC!(hDisp, 0u, ref info) == NVAPI_OK ? info.currentLevel : int.MinValue;
    }

    // ── Loader ────────────────────────────────────────────────────────────────

    private static bool Load()
    {
        try
        {
            IntPtr lib = LoadLibraryW("nvapi64.dll");
            if (lib == IntPtr.Zero)
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                lib = LoadLibraryW(System.IO.Path.Combine(sys32, "nvapi64.dll"));
            }
            if (lib == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _lastError = $"LoadLibrary(nvapi64.dll) failed, Win32 error {err}";
                return false;
            }

            IntPtr qiPtr = GetProcAddress(lib, "nvapi_QueryInterface");
            if (qiPtr == IntPtr.Zero)
            {
                _lastError = "nvapi_QueryInterface export not found";
                return false;
            }
            _queryInterface = Marshal.GetDelegateForFunctionPointer<Del_QueryInterface>(qiPtr);

            // NvAPI_Initialize
            IntPtr initPtr = _queryInterface(FID_Initialize);
            if (initPtr == IntPtr.Zero) { _lastError = "NvAPI_Initialize: null pointer"; return false; }
            int initRc = Marshal.GetDelegateForFunctionPointer<Del_Initialize>(initPtr)();
            if (initRc != NVAPI_OK) { _lastError = $"NvAPI_Initialize returned {initRc}"; return false; }

            // Resolve functions (same FIDs as vibranceGUI)
            _getAssocHandle = GetFn<Del_GetAssocHandle>(FID_GetAssociatedNvidiaDisplayHandle);
            _enumDisplay    = GetFn<Del_EnumDisplay>(FID_EnumNvidiaDisplayHandle);
            _getDisplayName = GetFn<Del_GetDisplayName>(FID_GetAssociatedNvidiaDisplayName);
            _getDVC         = GetFn<Del_GetDVCInfo>(FID_GetDVCInfo);
            _setDVC         = GetFn<Del_SetDVCLevel>(FID_SetDVCLevel);

            if (_getDVC is null) { _lastError = "NvAPI_GetDVCInfo (0x4085DE45): null pointer"; return false; }
            if (_setDVC is null) { _lastError = "NvAPI_SetDVCLevel (0x172409B4): null pointer"; return false; }

            _lastError = "OK";
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static T? GetFn<T>(uint id) where T : Delegate
    {
        try
        {
            IntPtr p = _queryInterface!(id);
            return p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
        }
        catch { return null; }
    }

    // ── Handle lookup ──────────────────────────────────────────────────────────

    private static IntPtr GetDisplayHandle(string? deviceName)
    {
        bool hasTgt = !string.IsNullOrWhiteSpace(deviceName);

        // Fast path: direct GDI name → NvAPI handle ("\\.\DISPLAY1" etc.)
        if (hasTgt && _getAssocHandle is not null)
        {
            int rc = _getAssocHandle(deviceName!, out IntPtr h);
            if (rc == NVAPI_OK && h != IntPtr.Zero) return h;
        }

        // Fallback: enumerate all NVIDIA display handles
        if (_enumDisplay is null) return IntPtr.Zero;

        string? shortTarget = hasTgt
            ? deviceName!.TrimStart('\\').Replace("\\", "").Replace(".", "").ToUpperInvariant()
            : null;

        IntPtr firstHandle = IntPtr.Zero;
        for (uint i = 0; i < 64; i++)
        {
            if (_enumDisplay(i, out IntPtr h) != NVAPI_OK || h == IntPtr.Zero) break;
            if (i == 0) firstHandle = h;
            if (shortTarget is null) return h;

            if (_getDisplayName is not null)
            {
                IntPtr buf = Marshal.AllocHGlobal(64);
                try
                {
                    if (_getDisplayName(h, buf) == NVAPI_OK)
                    {
                        string nvName = Marshal.PtrToStringAnsi(buf) ?? "";
                        string shortNv = nvName.TrimStart('\\').Replace("\\", "").Replace(".", "").ToUpperInvariant();
                        if (shortNv == shortTarget) return h;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        return firstHandle;
    }
}
