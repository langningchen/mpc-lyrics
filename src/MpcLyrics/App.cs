using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MpcLyrics.Core;
using MpcLyrics.Native;
using MpcLyrics.Services;
using MpcLyrics.UI;

namespace MpcLyrics;

public partial class App : Application
{
    private AppHost? _host;
    private SettingsWindow? _smokeWindow;

    public App()
    {
        AppLogger.Startup("App constructor entered");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var error = args.ExceptionObject as Exception
                        ?? new Exception(args.ExceptionObject?.ToString());
            AppLogger.Crash("AppDomain.UnhandledException", error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Crash("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        try
        {
            // Load WinUI 3 theme resources and the compiled XAML views.
            InitializeComponent();
            AppLogger.Startup("App.InitializeComponent completed");
            DebugSettings.XamlResourceReferenceFailed += (_, eventArgs) =>
                AppLogger.Startup($"XAML resource lookup failed: {eventArgs.Message}");
            DebugSettings.BindingFailed += (_, eventArgs) =>
                AppLogger.Startup($"XAML binding failed: {eventArgs.Message}");
        }
        catch (Exception error)
        {
            AppLogger.Crash("App.InitializeComponent", error);
            NativeMethods.MessageBoxW(
                0,
                $"MPC Lyrics 无法初始化 WinUI 资源。\n\n{error.GetType().FullName}\n{error.Message}\n\n日志：{AppLogger.CrashLogPath}",
                "MPC Lyrics — WinUI 初始化失败",
                NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            throw;
        }

        UnhandledException += (_, args) =>
        {
            AppLogger.Crash("Microsoft.UI.Xaml.Application.UnhandledException", args.Exception);
            NativeMethods.MessageBoxW(
                0,
                $"MPC Lyrics 发生错误。\n\n{args.Exception.Message}\n\n日志：{AppLogger.CrashLogPath}",
                "MPC Lyrics",
                NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLogger.Startup("App.OnLaunched entered");

        if (string.Equals(
                Environment.GetEnvironmentVariable("MPC_LYRICS_SMOKE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            RunSettingsWindowSmokeTest();
            return;
        }

        try
        {
            var options = CommandLineOptions.Parse(Environment.GetCommandLineArgs().Skip(1));
            AppLogger.Startup(
                $"Command line parsed: files={options.MediaFiles.Count}, settings={options.ShowSettings}");

            var existing = OverlayWindow.FindExisting();
            AppLogger.Startup($"Existing overlay HWND: 0x{existing.ToInt64():X}");
            if (existing != 0)
            {
                foreach (var file in options.MediaFiles)
                    OverlayWindow.SendString(
                        existing,
                        OverlayWindow.AppOpenFile,
                        Path.GetFullPath(file));
                if (options.ShowSettings)
                    OverlayWindow.SendString(
                        existing,
                        OverlayWindow.AppShowSettings,
                        string.Empty);
                else if (options.MediaFiles.Count == 0)
                    OverlayWindow.SendString(
                        existing,
                        OverlayWindow.AppActivatePlayer,
                        string.Empty);
                AppLogger.Startup("Request forwarded to existing instance; exiting this instance");
                Exit();
                return;
            }

            var dispatcher = DispatcherQueue.GetForCurrentThread()
                             ?? throw new InvalidOperationException(
                                 "Unable to obtain the WinUI dispatcher queue.");
            _host = new AppHost(dispatcher);
            AppLogger.Startup("AppHost constructed");
            _host.Start(options);
            AppLogger.Startup("AppHost.Start completed");
        }
        catch (Exception error)
        {
            AppLogger.Crash("App.OnLaunched", error);
            NativeMethods.MessageBoxW(
                0,
                $"MPC Lyrics 启动失败。\n\n{error.GetType().FullName}\n{error.Message}\n\n日志：{AppLogger.CrashLogPath}",
                "MPC Lyrics",
                NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            Exit();
        }
    }
    private async void RunSettingsWindowSmokeTest()
    {
        AppLogger.Startup("SMOKE_TEST: begin");
        try
        {
            LrcParser.ExerciseForSmokeTest();
            SubtitleLoader.ExerciseForSmokeTest();
            OverlayRenderer.ExerciseForSmokeTest();
            OverlayWindow.ExerciseSystemAcrylicForSmokeTest();
            MpcController.ExerciseForSmokeTest();
            var settings = AppSettings.Default();
            _smokeWindow = new SettingsWindow(
                settings,
                changed: static () => { },
                reset: static () => { },
                closed: static () => { },
                exit: static () => { });
            _smokeWindow.Activate();
            AppLogger.Startup("SMOKE_TEST: SettingsWindow activated");

            // Realize each editor flyout and all native ColorPicker templates.
            await Task.Delay(300);
            await _smokeWindow.ExerciseEditorFlyoutsForSmokeTest();
            await Task.Delay(300);

            _smokeWindow.Close();
            _smokeWindow = null;
            AppLogger.Startup("SMOKE_TEST: PASS");
            Environment.ExitCode = 0;
            Exit();
        }
        catch (Exception error)
        {
            AppLogger.Crash("SMOKE_TEST: SettingsWindow", error);
            Environment.ExitCode = 91;
            Exit();
        }
    }

}
