using System;
using System.Runtime.InteropServices;

namespace Zenith.Core
{
    internal static class Native
    {
        // ---------- CPU / memory ----------
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

        public static MEMORYSTATUSEX Memory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref m);
            return m;
        }

        // ---------- Recycle bin ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBin(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        public const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

        // ---------- System parameters ----------
        public const uint SPI_SETMOUSE = 0x0004;
        public const uint SPI_SETANIMATION = 0x0049;
        public const uint SPI_SETMENUSHOWDELAY = 0x006B;
        public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
        public const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint uiParam, int[] pvParam, uint winIni);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

        [StructLayout(LayoutKind.Sequential)]
        struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(uint action, uint uiParam, ref ANIMATIONINFO pvParam, uint winIni);

        public static void SetMouseAcceleration(bool enabled)
        {
            int[] p = enabled ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            SystemParametersInfo(SPI_SETMOUSE, 0, p, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        public static void SetAnimations(bool enabled)
        {
            var ai = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = enabled ? 1 : 0 };
            SystemParametersInfo(SPI_SETANIMATION, ai.cbSize, ref ai, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            SystemParametersInfo(SPI_SETCLIENTAREAANIMATION, 0, enabled ? new IntPtr(1) : IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        public static void SetMenuDelay(uint ms) =>
            SystemParametersInfo(SPI_SETMENUSHOWDELAY, ms, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

        // ---------- Broadcast settings change so Explorer picks up registry edits ----------
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        public static void BroadcastSettingChange(string area)
        {
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A /*WM_SETTINGCHANGE*/, IntPtr.Zero, area, 0x0002 /*SMTO_ABORTIFHUNG*/, 1000, out _);
        }

        // ---------- Memory list purge (same technique as RAMMap / ISLC) ----------
        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

        public const int SystemMemoryListInformation = 80;
        public const int MemoryEmptyWorkingSets = 2;
        public const int MemoryFlushModifiedList = 3;
        public const int MemoryPurgeStandbyList = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public uint LuidLow; public int LuidHigh; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, uint len, IntPtr prev, IntPtr retLen);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static bool EnablePrivilege(string name)
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /*ADJUST|QUERY*/, out var token)) return false;
            try
            {
                if (!LookupPrivilegeValue(null, name, out long luid)) return false;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    LuidLow = (uint)(luid & 0xFFFFFFFF),
                    LuidHigh = (int)(luid >> 32),
                    Attributes = 0x2 /*SE_PRIVILEGE_ENABLED*/
                };
                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(token); }
        }

        // ---------- Processes ----------
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, System.Text.StringBuilder exeName, ref int size);

        public static void CloseHandlePublic(IntPtr h) => CloseHandle(h);

        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetUserNameEx(int nameFormat, System.Text.StringBuilder name, ref uint size);

        // ---------- Window chrome (used by the UI) ----------
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---------- File icons (used by the UI) ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
