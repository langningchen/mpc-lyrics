using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MpcLyrics.Core;
using MpcLyrics.Native;
using MpcLyrics.Services;
using MpcLyrics.UI;
using Windows.Graphics;

namespace MpcLyrics;

public sealed class AppHost : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly OverlayRenderer _renderer = new();
    private readonly DispatcherQueueTimer _renderTimer;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Window _lifetimeWindow;
    private AppSettings _settings;
    private OverlayWindow? _overlay;
    private MpcController? _controller;
    private SettingsWindow? _settingsWindow;
    private LyricLine? _lastRenderedLine;
    private LyricLine? _lastRenderedNextLine;
    private int? _lastRenderedLineIndex;
    private string _status = "就绪";
    private bool _disposed;

    public AppHost(DispatcherQueue dispatcher)
    {
        AppLogger.Startup("AppHost constructor entered");
        _settings = _store.Load();
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(300);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _store.Save(_settings);
        };
        _lifetimeWindow = new Window
        {
            Content = new Grid { Visibility = Visibility.Collapsed },
        };
        _lifetimeWindow.AppWindow.MoveAndResize(new RectInt32(-32000, -32000, 1, 1));
        _lifetimeWindow.Activate();
        _lifetimeWindow.AppWindow.Hide();
        _renderTimer = dispatcher.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(100);
        _renderTimer.IsRepeating = true;
        _renderTimer.Tick += (_, _) => OnTimer();
        AppLogger.Startup("AppHost constructor completed");
    }

    public void Start(CommandLineOptions options)
    {
        AppLogger.Startup("AppHost.Start entered");
        _overlay = new OverlayWindow(_settings);
        AppLogger.Startup($"OverlayWindow created: 0x{_overlay.Hwnd.ToInt64():X}");
        _overlay.CopyDataReceived += OnCopyData;
        _overlay.SettingsRequested += ShowSettings;
        _overlay.RectChanged += rect =>
        {
            _settings.WindowX = rect.Left;
            _settings.WindowY = rect.Top;
            _settings.WindowWidth = rect.Width;
            _settings.WindowHeight = rect.Height;
            ScheduleSave();
            RefreshOverlay();
        };

        _controller = new MpcController(_overlay.Hwnd, _settings);
        _controller.DisplayChanged += OnControllerDisplayChanged;
        _controller.StatusChanged += status =>
        {
            _status = status;
            _settingsWindow?.SetStatus(status);
        };
        _controller.Disconnected += () =>
        {
            _status = "MPC-HC 已断开";
            _settingsWindow?.SetStatus(_status);
        };

        _renderTimer.Start();
        if (options.MediaFiles.Count > 0 || !options.ShowSettings)
        {
            try
            {
                _controller.Start(options.MediaFiles, options.PlayerPath);
            }
            catch (Exception error)
            {
                AppLogger.Log(error.ToString());
                _status = error.Message;
                NativeMethods.MessageBoxW(0, error.Message, "MPC Lyrics", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
                ShowSettings();
            }
        }
        else
        {
            ShowSettings();
        }

        if (options.ShowSettings) ShowSettings();
        RefreshOverlay();
        AppLogger.Startup("AppHost.Start completed");
    }

    private void OnCopyData(nuint command, string payload)
    {
        if (command == OverlayWindow.AppOpenFile)
        {
            if (_controller is null) return;
            try
            {
                if (_controller.IsConnected) _controller.OpenFile(payload);
                else _controller.Start(new[] { payload }, null);
            }
            catch (Exception error)
            {
                _status = error.Message;
                ShowSettings();
            }
            return;
        }
        if (command == OverlayWindow.AppShowSettings)
        {
            ShowSettings();
            return;
        }
        if (command == OverlayWindow.AppActivatePlayer)
        {
            ActivatePlayer();
            return;
        }
        _controller?.HandleCopyData(command, payload);
    }

    private void OnTimer()
    {
        try
        {
            if (_overlay?.InSizeMove == true) return;
            _controller?.PollPosition();
        }
        catch (Exception error)
        {
            AppLogger.Crash("AppHost.OnTimer", error);
            _status = error.Message;
            _settingsWindow?.SetStatus($"歌词渲染已暂停：{error.Message}");
            _renderTimer.Stop();
            _overlay?.Hide();
        }
    }

    private void RefreshOverlay()
    {
        if (_overlay is null || _overlay.InSizeMove) return;
        var rect = _overlay.GetRect();
        var line = _controller?.CurrentLine;
        var nextLine = _controller?.NextLine;
        var lineIndex = _controller?.CurrentLineIndex;
        var shouldShow = line is not null
                         || nextLine is not null
                         || !_settings.Locked;
        if (!shouldShow)
        {
            RememberRenderedContent(line, nextLine, lineIndex);
            _overlay.Hide();
            return;
        }

        using var bitmap = _renderer.Render(
            Math.Max(1, rect.Width),
            Math.Max(1, rect.Height),
            _settings,
            line,
            nextLine,
            lineIndex,
            _controller?.PositionMs ?? 0);
        _overlay.Present(bitmap);
        _overlay.Show();
        RememberRenderedContent(line, nextLine, lineIndex);
    }

    private void OnControllerDisplayChanged()
    {
        if (_controller is null) return;
        var line = _controller.CurrentLine;
        var nextLine = _controller.NextLine;
        var lineIndex = _controller.CurrentLineIndex;
        var contentChanged = !ReferenceEquals(line, _lastRenderedLine)
                             || !ReferenceEquals(nextLine, _lastRenderedNextLine)
                             || lineIndex != _lastRenderedLineIndex;
        if (!contentChanged && !_renderer.NeedsPositionUpdates) return;
        RefreshOverlay();
    }

    private void RememberRenderedContent(
        LyricLine? line,
        LyricLine? nextLine,
        int? lineIndex)
    {
        _lastRenderedLine = line;
        _lastRenderedNextLine = nextLine;
        _lastRenderedLineIndex = lineIndex;
    }

    private void ApplySettings()
    {
        _settings.Normalize();
        _overlay?.ApplySettings(_settings, reposition: false);
        ScheduleSave();
        RefreshOverlay();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ShowSettings()
    {
        AppLogger.Startup("ShowSettings requested");
        _overlay?.EnsureVisible();
        if (_settingsWindow is not null)
        {
            _settingsWindow.Reload();
            if (_overlay is not null) _settingsWindow.ShowCentered(_overlay.GetRect());
            _settingsWindow.Activate();
            _settingsWindow.SetStatus(_status);
            return;
        }

        try
        {
            AppLogger.Startup("Constructing SettingsWindow");
            _settingsWindow = new SettingsWindow(
                _settings,
                changed: ApplySettings,
                reset: ResetSettings,
                closed: OnSettingsClosed,
                exit: ExitApplication,
                anchor: _overlay?.GetRect());
            _settingsWindow.SetStatus(_status);
            _settingsWindow.Activate();
            AppLogger.Startup("SettingsWindow activated");
        }
        catch
        {
            _settingsWindow = null;
            RefreshOverlay();
            throw;
        }
    }

    private void ActivatePlayer()
    {
        if (_controller is null) return;
        try
        {
            if (_controller.IsConnected) _controller.ActivatePlayer();
            else _controller.Start(Array.Empty<string>(), null);
        }
        catch (Exception error)
        {
            _status = error.Message;
            AppLogger.Log(error.ToString());
            ShowSettings();
        }
    }

    private void OnSettingsClosed()
    {
        AppLogger.Startup("AppHost observed settings window close");
        _settingsWindow = null;
        if (!_disposed)
        {
            RefreshOverlay();
        }
    }


    private void ExitApplication()
    {
        Dispose();
        Application.Current.Exit();
    }

    private void ResetSettings()
    {
        var defaults = AppSettings.Default();
        CopySettings(defaults, _settings);
        _overlay?.ApplySettings(_settings, reposition: true);
        _renderer.ResetScroll();
        ScheduleSave();
        RefreshOverlay();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.WindowX = source.WindowX;
        target.WindowY = source.WindowY;
        target.WindowWidth = source.WindowWidth;
        target.WindowHeight = source.WindowHeight;
        target.Locked = source.Locked;
        target.AlwaysOnTop = source.AlwaysOnTop;
        target.PlayerPath = source.PlayerPath;
        target.OriginalEnabled = source.OriginalEnabled;
        target.TranslationEnabled = source.TranslationEnabled;
        target.OriginalHideWhenEmpty = source.OriginalHideWhenEmpty;
        target.TranslationHideWhenEmpty = source.TranslationHideWhenEmpty;
        target.OriginalFontSize = source.OriginalFontSize;
        target.TranslationFontSize = source.TranslationFontSize;
        target.OriginalOutlineWidth = source.OriginalOutlineWidth;
        target.TranslationOutlineWidth = source.TranslationOutlineWidth;
        target.OriginalBold = source.OriginalBold;
        target.OriginalItalic = source.OriginalItalic;
        target.TranslationBold = source.TranslationBold;
        target.TranslationItalic = source.TranslationItalic;
        target.OriginalAlignment = source.OriginalAlignment;
        target.TranslationAlignment = source.TranslationAlignment;
        target.OriginalTextColor = source.OriginalTextColor;
        target.OriginalTextColorMode = source.OriginalTextColorMode;
        target.OriginalOutlineColor = source.OriginalOutlineColor;
        target.OriginalOutlineColorMode = source.OriginalOutlineColorMode;
        target.TranslationTextColor = source.TranslationTextColor;
        target.TranslationTextColorMode = source.TranslationTextColorMode;
        target.TranslationOutlineColor = source.TranslationOutlineColor;
        target.TranslationOutlineColorMode = source.TranslationOutlineColorMode;
        target.NextOriginalEnabled = source.NextOriginalEnabled;
        target.NextTranslationEnabled = source.NextTranslationEnabled;
        target.NextOriginalHideWhenEmpty = source.NextOriginalHideWhenEmpty;
        target.NextTranslationHideWhenEmpty = source.NextTranslationHideWhenEmpty;
        target.NextLinePosition = source.NextLinePosition;
        target.AlternateNextLinePosition = source.AlternateNextLinePosition;
        target.NextOriginalFontSize = source.NextOriginalFontSize;
        target.NextTranslationFontSize = source.NextTranslationFontSize;
        target.NextOriginalOutlineWidth = source.NextOriginalOutlineWidth;
        target.NextTranslationOutlineWidth = source.NextTranslationOutlineWidth;
        target.NextOriginalBold = source.NextOriginalBold;
        target.NextOriginalItalic = source.NextOriginalItalic;
        target.NextTranslationBold = source.NextTranslationBold;
        target.NextTranslationItalic = source.NextTranslationItalic;
        target.NextOriginalAlignment = source.NextOriginalAlignment;
        target.NextTranslationAlignment = source.NextTranslationAlignment;
        target.NextOriginalTextColor = source.NextOriginalTextColor;
        target.NextOriginalTextColorMode = source.NextOriginalTextColorMode;
        target.NextOriginalOutlineColor = source.NextOriginalOutlineColor;
        target.NextOriginalOutlineColorMode = source.NextOriginalOutlineColorMode;
        target.NextTranslationTextColor = source.NextTranslationTextColor;
        target.NextTranslationTextColorMode = source.NextTranslationTextColorMode;
        target.NextTranslationOutlineColor = source.NextTranslationOutlineColor;
        target.NextTranslationOutlineColorMode = source.NextTranslationOutlineColorMode;
        target.BackgroundColor = source.BackgroundColor;
        target.BackgroundColorMode = source.BackgroundColorMode;
        target.BackgroundMode = source.BackgroundMode;
        target.BackgroundImagePath = source.BackgroundImagePath;
        target.BackgroundImageFill = source.BackgroundImageFill;
        target.BackgroundImageOpacity = source.BackgroundImageOpacity;
        target.AcrylicEnabled = source.AcrylicEnabled;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderTimer.Stop();
        _saveTimer.Stop();
        _store.Save(_settings);
        _renderer.Dispose();
        _overlay?.Dispose();
        _lifetimeWindow.Close();
    }
}
