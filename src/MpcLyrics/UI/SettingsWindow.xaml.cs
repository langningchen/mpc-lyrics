using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MpcLyrics.Core;
using MpcLyrics.Native;
using MpcLyrics.Services;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace MpcLyrics.UI;

public sealed partial class SettingsWindow : Window
{
    private const int WindowWidth = 560;
    private const int WindowHeight = 160;
    private const nuint FixedWindowSubclassId = 0x4D50_434C;

    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly Action _reset;
    private readonly Action _closed;
    private readonly Action _exit;
    private readonly NativeMethods.SubclassProc _windowSubclassProc;
    private bool _updating = true;
    private bool _windowSubclassInstalled;
    private double _rasterizationScale = 1d;
    private NativeMethods.RECT? _lastAnchor;
    private DispatcherQueueTimer? _statusScrollTimer;
    private double _statusScrollOffset;
    private int _statusScrollPauseTicks;
    private bool _statusScrollResetAfterPause;

    internal SettingsWindow(
        AppSettings settings,
        Action changed,
        Action reset,
        Action closed,
        Action exit,
        NativeMethods.RECT? anchor = null)
    {
        AppLogger.Startup("Settings window constructor entered");
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        _closed = closed ?? throw new ArgumentNullException(nameof(closed));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _windowSubclassProc = SettingsWindowSubclassProcedure;

        try
        {
            InitializeComponent();
            AppLogger.Startup("Settings window InitializeComponent completed");
        }
        catch (Exception error)
        {
            AppLogger.Startup(
                $"Settings window InitializeComponent failed: HRESULT=0x{error.HResult:X8}, " +
                $"message={error.Message}, inner={error.InnerException?.Message ?? "<none>"}");
            AppLogger.Crash("SettingsWindow.InitializeComponent", error);
            throw;
        }

        OriginalEditor.Configure(_settings, LyricTextTrack.Original, "文字", CommitChange);
        TranslationEditor.Configure(_settings, LyricTextTrack.Translation, "翻译", CommitChange);
        NextOriginalEditor.Configure(_settings, LyricTextTrack.NextOriginal, "下句文字", CommitChange);
        NextTranslationEditor.Configure(_settings, LyricTextTrack.NextTranslation, "下句翻译", CommitChange);

        ConfigureStatusScroller();
        ConfigureAsStandardWindow();
        Activated += (_, _) =>
        {
            ApplyFixedWindowCapabilities();
            ApplyWindowChromeColors();
        };
        Reload();
        if (anchor is { } rect) ShowCentered(rect);
        Closed += SettingsWindow_Closed;
        AppLogger.Startup("Settings window constructor completed");
    }

    public void SetStatus(string status)
    {
        const string loadedPrefix = "已加载：";
        var displayText = string.IsNullOrWhiteSpace(status) ? "修改会实时显示" : status.Trim();
        if (displayText.StartsWith(loadedPrefix, StringComparison.Ordinal))
            displayText = displayText[loadedPrefix.Length..].TrimStart();
        StatusText.Text = displayText;
        StatusText.Width = double.NaN;
        StatusText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        StatusText.Width = Math.Ceiling(StatusText.DesiredSize.Width);
        ResetStatusScroll();
    }

    internal void ShowCentered(NativeMethods.RECT anchor)
    {
        _lastAnchor = anchor;
        var physicalWidth = (int)Math.Ceiling(WindowWidth * _rasterizationScale);
        var physicalHeight = (int)Math.Ceiling(WindowHeight * _rasterizationScale);
        var anchorRect = new RectInt32(
            anchor.Left,
            anchor.Top,
            Math.Max(1, anchor.Width),
            Math.Max(1, anchor.Height));
        var display = DisplayArea.GetFromRect(anchorRect, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var x = work.X + Math.Max(0, (work.Width - physicalWidth) / 2);
        var y = work.Y + Math.Max(0, (work.Height - physicalHeight) / 2);

        UpdateFlyoutPlacement(x, y, physicalWidth, physicalHeight, work);
        AppWindow.MoveAndResize(new RectInt32(x, y, physicalWidth, physicalHeight));
        ApplyWindowChromeColors();
    }

    internal async Task ExerciseEditorFlyoutsForSmokeTest()
    {
        ExerciseFixedWindowCapabilitiesForSmokeTest();
        await ExerciseFlyout(WindowFlyout, WindowCategoryButton);
        await ExerciseFlyout(LayoutFlyout, LayoutCategoryButton);
        await ExerciseFlyout(MoreFlyout, MoreButton);

        await ExerciseTextFlyout(OriginalFlyout, OriginalButton);
        await ExerciseTextFlyout(TranslationFlyout, TranslationButton);
        await ExerciseTextFlyout(NextOriginalFlyout, NextOriginalButton);
        await ExerciseTextFlyout(NextTranslationFlyout, NextTranslationButton);

        BackgroundFlyout.ShowAt(BackgroundButton);
        await Task.Delay(220);
        var previousBackgroundMode = _settings.BackgroundMode;
        var previousColorMode = _settings.BackgroundColorMode;
        var previousAcrylicEnabled = _settings.AcrylicEnabled;
        _settings.BackgroundMode = LyricsBackgroundMode.Image;
        UpdateBackgroundModePanels();
        await Task.Delay(120);
        if (Math.Abs(BackgroundModeContent.ActualHeight - 470d) > 0.5d)
            throw new InvalidOperationException("Background editor height changed in image mode.");
        _settings.BackgroundMode = LyricsBackgroundMode.SolidColor;
        _settings.BackgroundColorMode = ColorSourceMode.SystemAccent;
        _settings.AcrylicEnabled = OverlayWindow.IsSystemAcrylicSupported;
        Reload();
        await Task.Delay(120);
        if (Math.Abs(BackgroundModeContent.ActualHeight - 470d) > 0.5d)
            throw new InvalidOperationException("Background editor height changed in solid mode.");
        if (BackgroundColorPicker.IsEnabled
            || AcrylicToggle.Visibility != (OverlayWindow.IsSystemAcrylicSupported
                ? Visibility.Visible
                : Visibility.Collapsed))
            throw new InvalidOperationException("Dynamic background-color controls are inconsistent.");
        _settings.BackgroundMode = previousBackgroundMode;
        _settings.BackgroundColorMode = previousColorMode;
        _settings.AcrylicEnabled = previousAcrylicEnabled;
        Reload();
        UpdateBackgroundModePanels();
        BackgroundFlyout.Hide();

        SetStatus(
            "已加载：A deliberately very long subtitle filename used to verify " +
            "automatic status scrolling in the standard settings window.srt");
        await Task.Delay(2000);
        if (StatusText.Text.StartsWith("已加载：", StringComparison.Ordinal))
            throw new InvalidOperationException("Loaded status prefix was not removed.");
        if (StatusScroller.ScrollableWidth <= 0.5 || StatusScroller.HorizontalOffset <= 0.5)
            throw new InvalidOperationException(
                $"Long status text did not scroll: text={StatusText.ActualWidth:0.##}, " +
                $"viewport={StatusScroller.ViewportWidth:0.##}, " +
                $"scrollable={StatusScroller.ScrollableWidth:0.##}, " +
                $"offset={StatusScroller.HorizontalOffset:0.##}.");
        SetStatus("修改会实时显示");
        AppLogger.Startup("SMOKE_TEST: all settings flyouts realized");
    }

    private static async Task ExerciseFlyout(Flyout flyout, FrameworkElement target)
    {
        flyout.ShowAt(target);
        await Task.Delay(180);
        flyout.Hide();
    }

    private async Task ExerciseTextFlyout(Flyout flyout, FrameworkElement target)
    {
        TextCategoryFlyout.ShowAt(TextCategoryButton);
        await Task.Delay(140);
        flyout.ShowAt(target);
        await Task.Delay(220);
        if (ReferenceEquals(flyout, OriginalFlyout)) OriginalEditor.ExerciseColorModesForSmokeTest();
        else if (ReferenceEquals(flyout, TranslationFlyout)) TranslationEditor.ExerciseColorModesForSmokeTest();
        else if (ReferenceEquals(flyout, NextOriginalFlyout)) NextOriginalEditor.ExerciseColorModesForSmokeTest();
        else if (ReferenceEquals(flyout, NextTranslationFlyout)) NextTranslationEditor.ExerciseColorModesForSmokeTest();
        flyout.Hide();
        TextCategoryFlyout.Hide();
    }

    public void Reload()
    {
        _updating = true;
        try
        {
            _settings.Normalize();
            LockedToggle.IsOn = _settings.Locked;
            TopmostToggle.IsOn = _settings.AlwaysOnTop;
            OriginalEditor.Reload();
            TranslationEditor.Reload();
            NextOriginalEditor.Reload();
            NextTranslationEditor.Reload();
            NextLinePositionCombo.SelectedIndex = (int)_settings.NextLinePosition;
            AlternatePositionToggle.IsOn = _settings.AlternateNextLinePosition;
            UpdateTrackButtonStates();

            ReloadBackgroundColorControls();
            AcrylicToggle.Visibility = OverlayWindow.IsSystemAcrylicSupported
                ? Visibility.Visible
                : Visibility.Collapsed;
            AcrylicToggle.IsOn = OverlayWindow.IsSystemAcrylicSupported
                                  && _settings.AcrylicEnabled;
            BackgroundImageFillCombo.SelectedIndex = (int)_settings.BackgroundImageFill;
            BackgroundImageOpacitySlider.Value = _settings.BackgroundImageOpacity * 100;
            BackgroundImageOpacityValue.Text = $"{Math.Round(_settings.BackgroundImageOpacity * 100):0}%";
            BackgroundImagePathText.Text = string.IsNullOrWhiteSpace(_settings.BackgroundImagePath)
                ? "尚未选择图片"
                : _settings.BackgroundImagePath;
            UpdateBackgroundModePanels();
        }
        finally
        {
            _updating = false;
        }
    }

    private void ConfigureAsStandardWindow()
    {
        Title = "MPC Lyrics 设置";
        ExtendsContentIntoTitleBar = false;
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, true);
        AppWindow.SetPresenter(presenter);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = true;
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        ApplyFixedWindowCapabilities();
        ApplyWindowChromeColors();
    }

    private void ApplyFixedWindowCapabilities()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == 0) return;
        if (!_windowSubclassInstalled)
        {
            _windowSubclassInstalled = NativeMethods.SetWindowSubclass(
                hwnd,
                _windowSubclassProc,
                FixedWindowSubclassId,
                0);
            if (!_windowSubclassInstalled)
                throw new InvalidOperationException("Unable to secure the settings window system commands.");
        }

        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        var fixedStyle = style
                         & ~(long)NativeMethods.WS_MAXIMIZEBOX
                         & ~(long)NativeMethods.WS_THICKFRAME;
        if (fixedStyle != style)
        {
            NativeMethods.SetWindowLongPtr(
                hwnd,
                NativeMethods.GWL_STYLE,
                new nint(fixedStyle));
        }
        NativeMethods.SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE
            | NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_FRAMECHANGED);
    }

    private static nint SettingsWindowSubclassProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == NativeMethods.WM_SYSCOMMAND
            && (wParam & 0xFFF0u) == NativeMethods.SC_MAXIMIZE)
        {
            return 0;
        }
        if (message == NativeMethods.WM_NCLBUTTONDBLCLK
            && (int)wParam == NativeMethods.HTCAPTION)
        {
            return 0;
        }
        return NativeMethods.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ExerciseFixedWindowCapabilitiesForSmokeTest()
    {
        ApplyFixedWindowCapabilities();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == 0) throw new InvalidOperationException("Settings HWND is unavailable.");

        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_MAXIMIZEBOX) != 0
            || (style & NativeMethods.WS_THICKFRAME) != 0)
        {
            throw new InvalidOperationException(
                $"Settings window retained maximize/resize styles: 0x{style:X}.");
        }
        if (AppWindow.Presenter is not OverlappedPresenter presenter
            || presenter.IsMaximizable
            || presenter.IsResizable)
        {
            throw new InvalidOperationException(
                "Settings presenter retained maximize/resize capabilities.");
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var before))
            throw new InvalidOperationException("Unable to read the settings window bounds.");
        NativeMethods.SendMessageW(
            hwnd,
            NativeMethods.WM_SYSCOMMAND,
            NativeMethods.SC_MAXIMIZE,
            0);
        AssertWindowDidNotMaximize(hwnd, before, "a system maximize command");
        NativeMethods.SendMessageW(
            hwnd,
            NativeMethods.WM_NCLBUTTONDBLCLK,
            (nuint)NativeMethods.HTCAPTION,
            0);
        AssertWindowDidNotMaximize(hwnd, before, "a title-bar double-click");
    }

    private static void AssertWindowDidNotMaximize(
        nint hwnd,
        NativeMethods.RECT before,
        string action)
    {
        if (NativeMethods.IsZoomed(hwnd)
            || !NativeMethods.GetWindowRect(hwnd, out var after)
            || before.Left != after.Left
            || before.Top != after.Top
            || before.Width != after.Width
            || before.Height != after.Height)
        {
            throw new InvalidOperationException($"Settings window accepted {action}.");
        }
    }

    private void ApplyWindowChromeColors()
    {
        var background = Windows.UI.Color.FromArgb(255, 32, 32, 32);
        var foreground = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        var inactiveForeground = Windows.UI.Color.FromArgb(180, 255, 255, 255);
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 58, 58, 58);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 72, 72, 72);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == 0) return;
        var borderColor = NativeMethods.MPC_SETTINGS_BACKGROUND_COLOR;
        var captionColor = NativeMethods.MPC_SETTINGS_BACKGROUND_COLOR;
        var textColor = NativeMethods.MPC_SETTINGS_TEXT_COLOR;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_BORDER_COLOR,
            ref borderColor,
            sizeof(uint));
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CAPTION_COLOR,
            ref captionColor,
            sizeof(uint));
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_TEXT_COLOR,
            ref textColor,
            sizeof(uint));
    }

    private void ConfigureStatusScroller()
    {
        _statusScrollTimer = DispatcherQueue.CreateTimer();
        _statusScrollTimer.Interval = TimeSpan.FromMilliseconds(33);
        _statusScrollTimer.IsRepeating = true;
        _statusScrollTimer.Tick += StatusScrollTimer_Tick;
        StatusScroller.SizeChanged += (_, _) => ResetStatusScroll();
        ResetStatusScroll();
        _statusScrollTimer.Start();
    }

    private void StatusScrollTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var scrollableWidth = StatusScroller.ScrollableWidth;
        if (scrollableWidth <= 0.5)
        {
            if (_statusScrollOffset > 0.01) ResetStatusScroll();
            return;
        }

        if (_statusScrollPauseTicks > 0)
        {
            _statusScrollPauseTicks--;
            if (_statusScrollPauseTicks == 0 && _statusScrollResetAfterPause)
            {
                _statusScrollOffset = 0;
                _statusScrollResetAfterPause = false;
                StatusScroller.ChangeView(0, null, null, true);
                _statusScrollPauseTicks = 10;
            }
            return;
        }

        _statusScrollOffset = Math.Min(scrollableWidth, _statusScrollOffset + 0.75);
        StatusScroller.ChangeView(_statusScrollOffset, null, null, true);
        if (_statusScrollOffset >= scrollableWidth - 0.01)
        {
            _statusScrollPauseTicks = 30;
            _statusScrollResetAfterPause = true;
        }
    }

    private void ResetStatusScroll()
    {
        _statusScrollOffset = 0;
        _statusScrollPauseTicks = 10;
        _statusScrollResetAfterPause = false;
        StatusScroller.ChangeView(0, null, null, true);
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        _rasterizationScale = Math.Max(1d, RootGrid.XamlRoot?.RasterizationScale ?? 1d);
        if (_lastAnchor is { } anchor)
        {
            ShowCentered(anchor);
        }
        else
        {
            var width = (int)Math.Ceiling(WindowWidth * _rasterizationScale);
            var height = (int)Math.Ceiling(WindowHeight * _rasterizationScale);
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeMethods.GetWindowRect(hwnd, out var currentRect);
            var display = DisplayArea.GetFromRect(
                new RectInt32(
                    currentRect.Left,
                    currentRect.Top,
                    Math.Max(1, currentRect.Width),
                    Math.Max(1, currentRect.Height)),
                DisplayAreaFallback.Primary);
            var work = display.WorkArea;
            var x = work.X + Math.Max(0, (work.Width - width) / 2);
            var y = work.Y + Math.Max(0, (work.Height - height) / 2);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
            UpdateFlyoutPlacement(x, y, width, height, work);
            ApplyWindowChromeColors();
        }
        Reload();
    }

    private void UpdateFlyoutPlacement(
        int windowX,
        int windowY,
        int windowWidth,
        int windowHeight,
        RectInt32 workArea)
    {
        var leftSpace = windowX - workArea.X;
        var rightSpace = workArea.X + workArea.Width - windowX - windowWidth;
        var horizontalPlacement = leftSpace > rightSpace
            ? FlyoutPlacementMode.Left
            : FlyoutPlacementMode.Right;
        OriginalFlyout.Placement = horizontalPlacement;
        TranslationFlyout.Placement = horizontalPlacement;
        NextOriginalFlyout.Placement = horizontalPlacement;
        NextTranslationFlyout.Placement = horizontalPlacement;

        var topSpace = windowY - workArea.Y;
        var bottomSpace = workArea.Y + workArea.Height - windowY - windowHeight;
        var verticalPlacement = topSpace > bottomSpace
            ? FlyoutPlacementMode.Top
            : FlyoutPlacementMode.Bottom;
        WindowFlyout.Placement = verticalPlacement;
        TextCategoryFlyout.Placement = verticalPlacement;
        LayoutFlyout.Placement = verticalPlacement;
        BackgroundFlyout.Placement = verticalPlacement;
        MoreFlyout.Placement = verticalPlacement;
    }

    private void CategoryFlyout_Opening(object sender, object args) => UpdateFlyoutPlacement();

    private void EditorFlyout_Opening(object sender, object args) => UpdateFlyoutPlacement();

    private void UpdateFlyoutPlacement()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return;
        var windowRect = new RectInt32(rect.Left, rect.Top, rect.Width, rect.Height);
        var display = DisplayArea.GetFromRect(windowRect, DisplayAreaFallback.Nearest);
        UpdateFlyoutPlacement(rect.Left, rect.Top, rect.Width, rect.Height, display.WorkArea);
    }

    private void EditorFlyout_Opened(object sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(sender, OriginalFlyout)) OriginalEditor.FocusEditor();
            else if (ReferenceEquals(sender, TranslationFlyout)) TranslationEditor.FocusEditor();
            else if (ReferenceEquals(sender, NextOriginalFlyout)) NextOriginalEditor.FocusEditor();
            else if (ReferenceEquals(sender, NextTranslationFlyout)) NextTranslationEditor.FocusEditor();
            else BackgroundFocusTarget.Focus(FocusState.Programmatic);
        });
    }

    private void LockedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.Locked = LockedToggle.IsOn;
        CommitChange();
    }

    private void TopmostToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.AlwaysOnTop = TopmostToggle.IsOn;
        CommitChange();
    }

    private void NextLinePositionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || NextLinePositionCombo.SelectedIndex < 0) return;
        _settings.NextLinePosition = (NextLinePosition)NextLinePositionCombo.SelectedIndex;
        CommitChange();
    }

    private void AlternatePositionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.AlternateNextLinePosition = AlternatePositionToggle.IsOn;
        CommitChange();
    }

    private void UpdateTrackButtonStates()
    {
        OriginalButton.Opacity = _settings.OriginalEnabled ? 1d : 0.5d;
        TranslationButton.Opacity = _settings.TranslationEnabled ? 1d : 0.5d;
        NextOriginalButton.Opacity = _settings.NextOriginalEnabled ? 1d : 0.5d;
        NextTranslationButton.Opacity = _settings.NextTranslationEnabled ? 1d : 0.5d;
    }

    private void SolidBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.BackgroundMode = LyricsBackgroundMode.SolidColor;
        UpdateBackgroundModePanels();
        CommitChange();
    }

    private void ImageBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.BackgroundMode = LyricsBackgroundMode.Image;
        UpdateBackgroundModePanels();
        CommitChange();
    }

    private void UpdateBackgroundModePanels()
    {
        var imageMode = _settings.BackgroundMode == LyricsBackgroundMode.Image;
        SolidBackgroundButton.IsChecked = !imageMode;
        ImageBackgroundButton.IsChecked = imageMode;
        SolidBackgroundPanel.Visibility = imageMode ? Visibility.Collapsed : Visibility.Visible;
        ImageBackgroundPanel.Visibility = imageMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackgroundColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_updating || _settings.BackgroundColorMode != ColorSourceMode.Custom) return;
        _settings.BackgroundColor = new RgbaColor(
            args.NewColor.R,
            args.NewColor.G,
            args.NewColor.B,
            _settings.BackgroundColor.A);
        UpdateBackgroundColorPreview();
        CommitChange();
    }

    private void BackgroundColorModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || BackgroundColorModeCombo.SelectedIndex < 0) return;
        _settings.BackgroundColorMode =
            (ColorSourceMode)BackgroundColorModeCombo.SelectedIndex;
        Reload();
        CommitChange();
    }

    private void BackgroundColorOpacitySlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        _settings.BackgroundColor = _settings.BackgroundColor.WithAlpha(
            PercentToAlpha(e.NewValue));
        BackgroundColorOpacityValue.Text = $"{e.NewValue:0}%";
        UpdateBackgroundColorPreview();
        CommitChange();
    }

    private void AcrylicToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _settings.AcrylicEnabled = AcrylicToggle.IsOn;
        CommitChange();
    }

    private async void ChooseBackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            _settings.BackgroundImagePath = file.Path;
            _settings.BackgroundMode = LyricsBackgroundMode.Image;
            BackgroundImagePathText.Text = file.Path;
            UpdateBackgroundModePanels();
            CommitChange();
        }
        catch (Exception error)
        {
            AppLogger.Log($"Unable to select background image: {error}");
            SetStatus("无法打开图片");
        }
    }

    private void BackgroundImageFillCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || BackgroundImageFillCombo.SelectedIndex < 0) return;
        _settings.BackgroundImageFill = (BackgroundImageFillMode)BackgroundImageFillCombo.SelectedIndex;
        CommitChange();
    }

    private void BackgroundImageOpacitySlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        _settings.BackgroundImageOpacity = (float)(e.NewValue / 100d);
        BackgroundImageOpacityValue.Text = $"{e.NewValue:0}%";
        CommitChange();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _reset();
        Reload();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e) => _exit();

    private void CommitChange()
    {
        if (_updating) return;
        _settings.Normalize();
        UpdateTrackButtonStates();
        _changed();
    }

    private void ReloadBackgroundColorControls()
    {
        var mode = _settings.BackgroundColorMode;
        BackgroundColorModeCombo.SelectedIndex = (int)mode;
        BackgroundColorPicker.IsEnabled = mode == ColorSourceMode.Custom;
        var pickerColor = ColorResolver.Resolve(mode, _settings.BackgroundColor).WithAlpha(255);
        BackgroundColorPicker.Color = pickerColor.ToWindowsColor();
        var opacity = _settings.BackgroundColor.A / 255d * 100d;
        BackgroundColorOpacitySlider.Value = opacity;
        BackgroundColorOpacityValue.Text = $"{opacity:0}%";
        UpdateBackgroundColorPreview();
    }

    private void UpdateBackgroundColorPreview()
    {
        BackgroundColorPreview.Background = new SolidColorBrush(
            ColorResolver.Resolve(
                _settings.BackgroundColorMode,
                _settings.BackgroundColor).ToWindowsColor());
    }

    private static byte PercentToAlpha(double percent) =>
        (byte)Math.Clamp((int)Math.Round(percent / 100d * 255d), 0, 255);

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        _statusScrollTimer?.Stop();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (_windowSubclassInstalled && hwnd != 0)
        {
            NativeMethods.RemoveWindowSubclass(
                hwnd,
                _windowSubclassProc,
                FixedWindowSubclassId);
            _windowSubclassInstalled = false;
        }
        AppLogger.Startup("Settings window closed");
        _closed();
    }
}
