using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using MpcLyrics.Core;
using MpcLyrics.Services;

namespace MpcLyrics.Native;

internal sealed class OverlayWindow : IDisposable
{
    public const string WindowClassName = "MpcLyrics.CSharp.Overlay.v1";
    private const string SystemAcrylicWindowClassName = "MpcLyrics.CSharp.SystemAcrylic.v1";
    public const nuint AppOpenFile = 0x7F00_0001;
    public const nuint AppShowSettings = 0x7F00_0002;
    public const nuint AppActivatePlayer = 0x7F00_0003;

    private const int ResizeBorder = 8;

    private static readonly ConcurrentDictionary<nint, OverlayWindow> Instances = new();
    private static readonly NativeMethods.WindowProc StaticWndProc = WindowProcedure;
    private static readonly NativeMethods.WindowProc StaticAcrylicWndProc = AcrylicWindowProcedure;
    private static readonly object ClassSync = new();
    private static readonly NativeMethods.LowLevelMouseProc StaticMouseHookProc = MouseHookProcedure;
    private static bool _classRegistered;
    private static nint _mouseHook;
    private static int _mouseHookUsers;
    private static nint _middleClickTarget;

    private AppSettings _settings;
    private nint _systemAcrylicHwnd;
    private bool _shown;
    private bool _disposed;
    private bool _inSizeMove;

    public OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        EnsureClassRegistered();
        var exStyle = NativeMethods.WS_EX_LAYERED
                      | NativeMethods.WS_EX_TOOLWINDOW
                      | NativeMethods.WS_EX_NOACTIVATE;
        if (settings.Locked) exStyle |= NativeMethods.WS_EX_TRANSPARENT;

        Hwnd = NativeMethods.CreateWindowExW(
            exStyle,
            WindowClassName,
            "MPC Lyrics",
            NativeMethods.WS_POPUP,
            settings.WindowX,
            settings.WindowY,
            settings.WindowWidth,
            settings.WindowHeight,
            0,
            0,
            NativeMethods.GetModuleHandleW(null),
            0);
        if (Hwnd == 0)
            throw new InvalidOperationException($"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");

        Instances[Hwnd] = this;
        var mouseHookInstalled = false;
        try
        {
            InstallMouseHook();
            mouseHookInstalled = true;
            ApplySettings(settings, reposition: true);
        }
        catch
        {
            if (mouseHookInstalled) ReleaseMouseHook();
            Instances.TryRemove(Hwnd, out _);
            NativeMethods.DestroyWindow(Hwnd);
            Hwnd = 0;
            throw;
        }
    }

    public nint Hwnd { get; private set; }
    public bool InSizeMove => _inSizeMove;
    public static bool IsSystemAcrylicSupported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public event Action<nuint, string>? CopyDataReceived;
    public event Action? SettingsRequested;
    public event Action<NativeMethods.RECT>? RectChanged;
    public event Action? Destroyed;

    public void ApplySettings(AppSettings settings, bool reposition)
    {
        _settings = settings;
        var style = (long)NativeMethods.GetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE);
        if (settings.Locked) style |= NativeMethods.WS_EX_TRANSPARENT;
        else style &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(Hwnd, NativeMethods.GWL_EXSTYLE, new nint(style));

        var insertAfter = settings.AlwaysOnTop
            ? NativeMethods.HWND_TOPMOST
            : NativeMethods.HWND_NOTOPMOST;
        var flags = NativeMethods.SWP_NOACTIVATE;
        if (!reposition) flags |= NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE;
        var target = new NativeMethods.RECT
        {
            Left = settings.WindowX,
            Top = settings.WindowY,
            Right = settings.WindowX + Math.Max(64, settings.WindowWidth),
            Bottom = settings.WindowY + Math.Max(20, settings.WindowHeight),
        };
        if (reposition)
        {
            target = ConstrainToNearestWorkArea(target);
            settings.WindowX = target.Left;
            settings.WindowY = target.Top;
            settings.WindowWidth = target.Width;
            settings.WindowHeight = target.Height;
        }
        NativeMethods.SetWindowPos(
            Hwnd,
            insertAfter,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            flags);
        ApplySystemAcrylic(settings, target);
    }

    public void EnsureVisible()
    {
        if (_disposed || Hwnd == 0 || !NativeMethods.GetWindowRect(Hwnd, out var current)) return;
        var target = ConstrainToNearestWorkArea(current);
        if (target.Left == current.Left
            && target.Top == current.Top
            && target.Width == current.Width
            && target.Height == current.Height)
        {
            return;
        }

        _settings.WindowX = target.Left;
        _settings.WindowY = target.Top;
        _settings.WindowWidth = target.Width;
        _settings.WindowHeight = target.Height;
        NativeMethods.SetWindowPos(
            Hwnd,
            0,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        SyncSystemAcrylic(target);
        NotifyRectChanged();
    }

    public void Show()
    {
        _shown = true;
        if (_systemAcrylicHwnd != 0)
        {
            NativeMethods.ShowWindow(_systemAcrylicHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        }
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        SyncSystemAcrylicToOverlay();
    }

    public void Hide()
    {
        _shown = false;
        NativeMethods.ShowWindow(Hwnd, NativeMethods.SW_HIDE);
        if (_systemAcrylicHwnd != 0)
        {
            NativeMethods.ShowWindow(_systemAcrylicHwnd, NativeMethods.SW_HIDE);
        }
    }

    private void ApplySystemAcrylic(AppSettings settings, NativeMethods.RECT target)
    {
        if (!settings.AcrylicEnabled || !IsSystemAcrylicSupported)
        {
            DestroySystemAcrylic();
            return;
        }

        if (_systemAcrylicHwnd == 0 && !TryCreateSystemAcrylic(target)) return;
        SyncSystemAcrylic(target);
        if (_shown)
            NativeMethods.ShowWindow(_systemAcrylicHwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    private bool TryCreateSystemAcrylic(NativeMethods.RECT target)
    {
        var hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TRANSPARENT,
            SystemAcrylicWindowClassName,
            string.Empty,
            NativeMethods.WS_POPUP,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            0,
            0,
            NativeMethods.GetModuleHandleW(null),
            0);
        if (hwnd == 0)
        {
            AppLogger.Log($"Unable to create system acrylic window: {Marshal.GetLastWin32Error()}");
            return false;
        }

        var margins = new NativeMethods.MARGINS
        {
            Left = -1,
            Right = -1,
            Top = -1,
            Bottom = -1,
        };
        var backdropType = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        var extendResult = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var backdropResult = NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdropType,
            sizeof(uint));
        if (extendResult != 0 || backdropResult != 0)
        {
            AppLogger.Log(
                $"Windows system acrylic unavailable: extend=0x{extendResult:X8}, " +
                $"backdrop=0x{backdropResult:X8}");
            NativeMethods.DestroyWindow(hwnd);
            return false;
        }

        _systemAcrylicHwnd = hwnd;
        return true;
    }

    private void SyncSystemAcrylicToOverlay()
    {
        if (_systemAcrylicHwnd != 0 && NativeMethods.GetWindowRect(Hwnd, out var rect))
            SyncSystemAcrylic(rect);
    }

    private void SyncSystemAcrylic(NativeMethods.RECT rect)
    {
        if (_systemAcrylicHwnd == 0) return;
        NativeMethods.SetWindowPos(
            _systemAcrylicHwnd,
            Hwnd,
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            NativeMethods.SWP_NOACTIVATE);
    }

    private void DestroySystemAcrylic()
    {
        if (_systemAcrylicHwnd == 0) return;
        NativeMethods.DestroyWindow(_systemAcrylicHwnd);
        _systemAcrylicHwnd = 0;
    }

    public void Present(Bitmap bitmap)
    {
        if (_disposed || Hwnd == 0 || bitmap.Width <= 0 || bitmap.Height <= 0) return;
        if (!NativeMethods.GetWindowRect(Hwnd, out var rect)) return;

        var screenDc = NativeMethods.GetDC(0);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        nint bitmapHandle = 0;
        nint oldObject = 0;
        try
        {
            bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
            oldObject = NativeMethods.SelectObject(memoryDc, bitmapHandle);
            var destination = new NativeMethods.POINT(rect.Left, rect.Top);
            var source = new NativeMethods.POINT(0, 0);
            var size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            if (!NativeMethods.UpdateLayeredWindow(
                    Hwnd, screenDc, ref destination, ref size, memoryDc,
                    ref source, 0, ref blend, NativeMethods.ULW_ALPHA))
            {
                AppLogger.Log($"UpdateLayeredWindow failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            if (oldObject != 0) NativeMethods.SelectObject(memoryDc, oldObject);
            if (bitmapHandle != 0) NativeMethods.DeleteObject(bitmapHandle);
            if (memoryDc != 0) NativeMethods.DeleteDC(memoryDc);
            if (screenDc != 0) NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    public NativeMethods.RECT GetRect()
    {
        NativeMethods.GetWindowRect(Hwnd, out var rect);
        return rect;
    }

    public static nint FindExisting() => NativeMethods.FindWindowW(WindowClassName, null);

    internal static void ExerciseSystemAcrylicForSmokeTest()
    {
        if (!IsSystemAcrylicSupported) return;
        var settings = AppSettings.Default();
        settings.WindowWidth = 96;
        settings.WindowHeight = 48;
        settings.AcrylicEnabled = true;
        using var overlay = new OverlayWindow(settings);
        overlay.Show();
        if (overlay._systemAcrylicHwnd == 0
            || ((long)NativeMethods.GetWindowLongPtr(
                    overlay._systemAcrylicHwnd,
                    NativeMethods.GWL_EXSTYLE)
                & NativeMethods.WS_EX_LAYERED) != 0
            || NativeMethods.SendMessageW(
                overlay._systemAcrylicHwnd,
                NativeMethods.WM_NCHITTEST,
                0,
                0) != NativeMethods.HTTRANSPARENT
            || NativeMethods.DwmGetWindowAttribute(
                overlay._systemAcrylicHwnd,
                NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                out var backdropType,
                sizeof(uint)) != 0
            || backdropType != NativeMethods.DWMSBT_TRANSIENTWINDOW
            || !NativeMethods.GetWindowRect(overlay.Hwnd, out var foreground)
            || !NativeMethods.GetWindowRect(overlay._systemAcrylicHwnd, out var background)
            || foreground.Left != background.Left
            || foreground.Top != background.Top
            || foreground.Width != background.Width
            || foreground.Height != background.Height)
        {
            throw new InvalidOperationException("Windows system acrylic backdrop failed.");
        }

        settings.WindowX += 12;
        settings.WindowY += 8;
        settings.WindowWidth += 24;
        settings.WindowHeight += 12;
        overlay.ApplySettings(settings, reposition: true);
        if (!NativeMethods.GetWindowRect(overlay.Hwnd, out foreground)
            || !NativeMethods.GetWindowRect(overlay._systemAcrylicHwnd, out background)
            || foreground.Left != background.Left
            || foreground.Top != background.Top
            || foreground.Width != background.Width
            || foreground.Height != background.Height)
        {
            throw new InvalidOperationException("System acrylic did not follow the overlay bounds.");
        }

        settings.AcrylicEnabled = false;
        overlay.ApplySettings(settings, reposition: false);
        if (overlay._systemAcrylicHwnd != 0)
            throw new InvalidOperationException("System acrylic window remained after being disabled.");
        overlay.Hide();
    }

    public static void SendString(nint target, nuint command, string text)
    {
        var units = (text + '\0').ToCharArray();
        var handle = GCHandle.Alloc(units, GCHandleType.Pinned);
        try
        {
            var data = new NativeMethods.COPYDATASTRUCT
            {
                dwData = command,
                cbData = checked((uint)(units.Length * sizeof(char))),
                lpData = handle.AddrOfPinnedObject(),
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(data, pointer, false);
                NativeMethods.SendMessageW(target, NativeMethods.WM_COPYDATA, 0, pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    private static void EnsureClassRegistered()
    {
        lock (ClassSync)
        {
            if (_classRegistered) return;
            RegisterWindowClass(WindowClassName, StaticWndProc);
            RegisterWindowClass(SystemAcrylicWindowClassName, StaticAcrylicWndProc);
            _classRegistered = true;
        }
    }

    private static void RegisterWindowClass(
        string className,
        NativeMethods.WindowProc windowProcedure)
    {
        var windowClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = checked((uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>()),
            style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW,
            lpfnWndProc = windowProcedure,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = NativeMethods.GetModuleHandleW(null),
            hIcon = 0,
            hCursor = NativeMethods.LoadCursorW(0, new nint(NativeMethods.IDC_ARROW)),
            hbrBackground = 0,
            lpszMenuName = string.Empty,
            lpszClassName = className,
            hIconSm = 0,
        };
        var atom = NativeMethods.RegisterClassExW(ref windowClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            const int ErrorClassAlreadyExists = 1410;
            if (error != ErrorClassAlreadyExists)
                throw new InvalidOperationException($"RegisterClassExW({className}) failed: {error}");
        }
    }

    private static nint AcrylicWindowProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (message == NativeMethods.WM_NCHITTEST) return NativeMethods.HTTRANSPARENT;
        if (message == NativeMethods.WM_ERASEBKGND) return 1;
        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }


    private static void InstallMouseHook()
    {
        lock (ClassSync)
        {
            _mouseHookUsers++;
            if (_mouseHook != 0) return;

            _mouseHook = NativeMethods.SetWindowsHookExW(
                NativeMethods.WH_MOUSE_LL,
                StaticMouseHookProc,
                NativeMethods.GetModuleHandleW(null),
                0);
            if (_mouseHook == 0)
            {
                _mouseHookUsers--;
                throw new InvalidOperationException(
                    $"SetWindowsHookExW(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
            }
        }
    }

    private static void ReleaseMouseHook()
    {
        lock (ClassSync)
        {
            if (_mouseHookUsers > 0) _mouseHookUsers--;
            if (_mouseHookUsers != 0 || _mouseHook == 0) return;

            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
            _middleClickTarget = 0;
        }
    }

    private static nint MouseHookProcedure(int code, nuint message, nint lParam)
    {
        var isMiddleButtonMessage = message == NativeMethods.WM_MBUTTONDOWN
                                    || message == NativeMethods.WM_MBUTTONDBLCLK
                                    || message == NativeMethods.WM_MBUTTONUP;
        if (code >= 0 && lParam != 0 && isMiddleButtonMessage)
        {
            var mouse = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            if (message == NativeMethods.WM_MBUTTONDOWN
                || message == NativeMethods.WM_MBUTTONDBLCLK)
            {
                var target = FindVisibleOverlayAt(mouse.pt);
                if (target is not null)
                {
                    _middleClickTarget = target.Hwnd;
                    return 1;
                }
            }
            else if (message == NativeMethods.WM_MBUTTONUP && _middleClickTarget != 0)
            {
                var targetHwnd = _middleClickTarget;
                _middleClickTarget = 0;

                // Always consume the matching button-up. If the pointer is still
                // inside the same visible overlay, ask its window procedure to open
                // settings. This avoids passing half of a middle click to the window
                // underneath when the user moves the pointer before releasing.
                if (Instances.TryGetValue(targetHwnd, out var target)
                    && IsPointInsideVisibleOverlay(target, mouse.pt))
                {
                    NativeMethods.PostMessageW(
                        target.Hwnd,
                        NativeMethods.WM_APP_SHOW_SETTINGS,
                        0,
                        0);
                }
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, message, lParam);
    }

    private static OverlayWindow? FindVisibleOverlayAt(NativeMethods.POINT point)
    {
        foreach (var window in Instances.Values)
        {
            if (IsPointInsideVisibleOverlay(window, point)) return window;
        }
        return null;
    }

    private static bool IsPointInsideVisibleOverlay(
        OverlayWindow window,
        NativeMethods.POINT point)
    {
        return !window._disposed
               && window.Hwnd != 0
               && NativeMethods.IsWindowVisible(window.Hwnd)
               && NativeMethods.GetWindowRect(window.Hwnd, out var rect)
               && point.X >= rect.Left
               && point.X < rect.Right
               && point.Y >= rect.Top
               && point.Y < rect.Bottom;
    }

    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (!Instances.TryGetValue(hwnd, out var window))
            return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
        return window.HandleMessage(message, wParam, lParam);
    }

    private nint HandleMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_COPYDATA:
                HandleCopyData(lParam);
                return 1;
            case NativeMethods.WM_APP_SHOW_SETTINGS:
                SettingsRequested?.Invoke();
                return 0;
            case NativeMethods.WM_NCHITTEST:
                return HitTest(lParam);
            case NativeMethods.WM_GETMINMAXINFO:
                unsafe
                {
                    var info = (NativeMethods.MINMAXINFO*)lParam.ToPointer();
                    info->ptMinTrackSize = new NativeMethods.POINT(64, 20);
                }
                return 0;
            case NativeMethods.WM_DISPLAYCHANGE:
                EnsureVisible();
                return 0;
            case NativeMethods.WM_ENTERSIZEMOVE:
                _inSizeMove = true;
                return 0;
            case NativeMethods.WM_EXITSIZEMOVE:
                _inSizeMove = false;
                NotifyRectChanged();
                return 0;
            case NativeMethods.WM_MOVE:
            case NativeMethods.WM_SIZE:
                SyncSystemAcrylicToOverlay();
                if (!_inSizeMove) NotifyRectChanged();
                return 0;
            case NativeMethods.WM_ERASEBKGND:
                return 1;
            case NativeMethods.WM_DESTROY:
                Destroyed?.Invoke();
                return 0;
        }
        return NativeMethods.DefWindowProcW(Hwnd, message, wParam, lParam);
    }

    private nint HitTest(nint lParam)
    {
        if (_settings.Locked) return NativeMethods.HTTRANSPARENT;
        if (!NativeMethods.GetWindowRect(Hwnd, out var rect)) return NativeMethods.HTCLIENT;
        var x = NativeMethods.LowWord(lParam);
        var y = NativeMethods.HighWord(lParam);
        var left = x < rect.Left + ResizeBorder;
        var right = x >= rect.Right - ResizeBorder;
        var top = y < rect.Top + ResizeBorder;
        var bottom = y >= rect.Bottom - ResizeBorder;
        if (top && left) return NativeMethods.HTTOPLEFT;
        if (top && right) return NativeMethods.HTTOPRIGHT;
        if (bottom && left) return NativeMethods.HTBOTTOMLEFT;
        if (bottom && right) return NativeMethods.HTBOTTOMRIGHT;
        if (left) return NativeMethods.HTLEFT;
        if (right) return NativeMethods.HTRIGHT;
        if (top) return NativeMethods.HTTOP;
        if (bottom) return NativeMethods.HTBOTTOM;
        return NativeMethods.HTCAPTION;
    }

    private void HandleCopyData(nint lParam)
    {
        if (lParam == 0) return;
        var data = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(lParam);
        var text = data.lpData == 0 || data.cbData < 2
            ? string.Empty
            : Marshal.PtrToStringUni(data.lpData, checked((int)data.cbData / 2))?.TrimEnd('\0') ?? string.Empty;
        CopyDataReceived?.Invoke(data.dwData, text);
    }

    private void NotifyRectChanged()
    {
        if (NativeMethods.GetWindowRect(Hwnd, out var rect)) RectChanged?.Invoke(rect);
    }

    private static NativeMethods.RECT ConstrainToNearestWorkArea(NativeMethods.RECT requested)
    {
        var monitorRect = requested;
        var monitor = NativeMethods.MonitorFromRect(
            ref monitorRect,
            NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == 0) return requested;

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = checked((uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()),
        };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return requested;

        var work = info.rcWork;
        var width = Math.Min(Math.Max(64, requested.Width), Math.Max(1, work.Width));
        var height = Math.Min(Math.Max(20, requested.Height), Math.Max(1, work.Height));
        var left = Math.Clamp(requested.Left, work.Left, work.Right - width);
        var top = Math.Clamp(requested.Top, work.Top, work.Bottom - height);
        return new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Hwnd != 0)
        {
            DestroySystemAcrylic();
            Instances.TryRemove(Hwnd, out _);
            NativeMethods.DestroyWindow(Hwnd);
            Hwnd = 0;
            ReleaseMouseHook();
        }
    }
}
