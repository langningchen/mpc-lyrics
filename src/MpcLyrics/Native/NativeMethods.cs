using System.Runtime.InteropServices;

namespace MpcLyrics.Native;

internal static class NativeMethods
{
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    public const uint WM_ENTERSIZEMOVE = 0x0231;
    public const uint WM_APP_SHOW_SETTINGS = 0x8001;
    public const uint WM_EXITSIZEMOVE = 0x0232;

    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_DLGFRAME = 0x00400000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const nuint SC_MAXIMIZE = 0xF030;
    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const uint ULW_ALPHA = 0x00000002;
    public const int WH_MOUSE_LL = 14;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_CAPTION_COLOR = 35;
    public const uint DWMWA_TEXT_COLOR = 36;
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint DWMSBT_NONE = 1;
    public const uint DWMSBT_TRANSIENTWINDOW = 3;
    public const uint MPC_SETTINGS_BACKGROUND_COLOR = 0x00202020;
    public const uint MPC_SETTINGS_TEXT_COLOR = 0x00FFFFFF;

    public const int IDC_ARROW = 32512;
    public const uint MB_OK = 0x00000000;
    public const uint MB_ICONERROR = 0x00000010;
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint LowLevelMouseProc(int code, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint SubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WindowProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx;
        public int Cy;
        public SIZE(int cx, int cy) { Cx = cx; Cy = cy; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public nuint dwData;
        public uint cbData;
        public nint lpData;
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(
        nint hwnd,
        nint screenDc,
        ref POINT destination,
        ref SIZE size,
        nint sourceDc,
        ref POINT source,
        uint colorKey,
        ref BLENDFUNCTION blend,
        uint flags);

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint obj);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint LoadCursorW(nint instance, nint cursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SetWindowsHookExW(
        int hookId,
        LowLevelMouseProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(
        nint hook,
        int code,
        nuint wParam,
        nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll")]
    public static extern nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(nint hwnd);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(
        nint hwnd,
        SubclassProc callback,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    public static extern bool RemoveWindowSubclass(
        nint hwnd,
        SubclassProc callback,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    public static extern nint DefSubclassProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(
        nint hwnd,
        uint attribute,
        ref uint value,
        uint valueSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(
        nint hwnd,
        uint attribute,
        out uint value,
        uint valueSize);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(
        nint hwnd,
        ref MARGINS margins);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);

    public static int LowWord(nint value) => unchecked((short)((long)value & 0xFFFF));
    public static int HighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xFFFF));
}
