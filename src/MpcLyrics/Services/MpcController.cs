using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MpcLyrics.Core;
using MpcLyrics.Native;

namespace MpcLyrics.Services;

public sealed class MpcController
{
    public const nuint CmdConnect = 0x5000_0000;
    public const nuint CmdState = 0x5000_0001;
    public const nuint CmdPlayMode = 0x5000_0002;
    public const nuint CmdNowPlaying = 0x5000_0003;
    public const nuint CmdCurrentPosition = 0x5000_0007;
    public const nuint CmdNotifySeek = 0x5000_0008;
    public const nuint CmdNotifyEndOfStream = 0x5000_0009;
    public const nuint CmdDisconnect = 0x5000_000B;

    public const nuint CmdOpenFile = 0xA000_0000;
    public const nuint CmdAddToPlaylist = 0xA000_1000;
    public const nuint CmdStartPlaylist = 0xA000_1002;
    public const nuint CmdGetNowPlaying = 0xA000_3002;
    public const nuint CmdGetCurrentPosition = 0xA000_3004;

    private readonly nint _receiverHwnd;
    private readonly AppSettings _settings;
    private readonly List<string> _pendingFiles = new();
    private nint _mpcHwnd;
    private string? _currentMedia;
    private LyricsDocument? _lyrics;
    private int? _lineIndex;
    private long _positionMs;

    public MpcController(nint receiverHwnd, AppSettings settings)
    {
        _receiverHwnd = receiverHwnd;
        _settings = settings;
    }

    public LyricsDocument? Lyrics => _lyrics;
    public int? CurrentLineIndex => _lineIndex;
    public long PositionMs => _positionMs;
    public LyricLine? CurrentLine => _lineIndex is int index && _lyrics is not null
        ? _lyrics.Lines[index]
        : null;
    public LyricLine? NextLine => _lineIndex is int index
                                  && _lyrics is not null
                                  && index + 1 < _lyrics.Lines.Count
        ? _lyrics.Lines[index + 1]
        : null;
    public bool IsConnected => _mpcHwnd != 0 && NativeMethods.IsWindow(_mpcHwnd);

    public event Action? DisplayChanged;
    public event Action<string>? StatusChanged;
    public event Action? Disconnected;

    public void ActivatePlayer()
    {
        if (IsConnected) NativeMethods.SetForegroundWindow(_mpcHwnd);
    }

    public void Start(IEnumerable<string> mediaFiles, string? explicitPlayerPath)
    {
        _pendingFiles.Clear();
        _pendingFiles.AddRange(mediaFiles.Where(File.Exists).Select(Path.GetFullPath));
        var player = LocatePlayer(explicitPlayerPath);
        if (player is null)
            throw new FileNotFoundException(
                "MPC-HC was not found. Use --player <path>, set PlayerPath in settings.json, " +
                "or install MPC-HC in a standard Program Files, LocalAppData, Scoop, or Chocolatey location.");

        AppLogger.Log($"Using MPC-HC executable: {player}");

        var startInfo = new ProcessStartInfo
        {
            FileName = player,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(player) ?? AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("/slave");
        startInfo.ArgumentList.Add(((long)_receiverHwnd).ToString(CultureInfo.InvariantCulture));
        Process.Start(startInfo);
        StatusChanged?.Invoke($"已启动 {Path.GetFileName(player)}，等待连接……");
    }

    public void HandleCopyData(nuint command, string payload)
    {
        switch (command)
        {
            case CmdConnect:
                HandleConnect(payload);
                break;
            case CmdNowPlaying:
                HandleNowPlaying(payload);
                break;
            case CmdCurrentPosition:
            case CmdNotifySeek:
                HandlePosition(payload);
                break;
            case CmdState:
                if (payload.Trim() == "0") ClearLyrics();
                break;
            case CmdNotifyEndOfStream:
                ClearCurrentLine();
                break;
            case CmdDisconnect:
                _mpcHwnd = 0;
                Disconnected?.Invoke();
                break;
        }
    }

    public void PollPosition()
    {
        if (_mpcHwnd == 0) return;
        if (!NativeMethods.IsWindow(_mpcHwnd))
        {
            _mpcHwnd = 0;
            Disconnected?.Invoke();
            return;
        }
        SendCommand(_mpcHwnd, CmdGetCurrentPosition, string.Empty);
    }

    public void OpenFile(string path)
    {
        if (!File.Exists(path)) return;
        path = Path.GetFullPath(path);
        if (_mpcHwnd == 0)
        {
            _pendingFiles.Clear();
            _pendingFiles.Add(path);
            return;
        }
        SendCommand(_mpcHwnd, CmdOpenFile, path);
        NativeMethods.SetForegroundWindow(_mpcHwnd);
    }

    private void HandleConnect(string payload)
    {
        if (!TryParseHwnd(payload, out _mpcHwnd))
        {
            AppLogger.Log($"Invalid MPC CMD_CONNECT payload: {payload}");
            return;
        }
        AppLogger.Log($"Connected to MPC-HC HWND {_mpcHwnd}");
        SendCommand(_mpcHwnd, CmdGetNowPlaying, string.Empty);
        if (_pendingFiles.Count > 0)
        {
            SendCommand(_mpcHwnd, CmdOpenFile, _pendingFiles[0]);
            for (var index = 1; index < _pendingFiles.Count; index++)
                SendCommand(_mpcHwnd, CmdAddToPlaylist, _pendingFiles[index]);
            if (_pendingFiles.Count > 1)
                SendCommand(_mpcHwnd, CmdStartPlaylist, string.Empty);
            _pendingFiles.Clear();
        }
        StatusChanged?.Invoke("已连接 MPC-HC");
    }

    private void HandleNowPlaying(string payload)
    {
        var fields = SplitMpcFields(payload);
        if (fields.Count <= 3) return;
        var filename = fields[3];
        if (string.IsNullOrWhiteSpace(filename))
        {
            ClearLyrics();
            return;
        }

        var fullPath = Path.GetFullPath(filename);
        if (string.Equals(_currentMedia, fullPath, StringComparison.OrdinalIgnoreCase)) return;
        _currentMedia = fullPath;
        try
        {
            _lyrics = SubtitleLoader.LoadForMedia(fullPath);
            _lineIndex = null;
            _positionMs = 0;
            if (_lyrics is null)
            {
                StatusChanged?.Invoke($"未找到同名字幕：{Path.GetFileName(fullPath)}");
                AppLogger.Log($"No matching subtitle for {fullPath}");
            }
            else
            {
                StatusChanged?.Invoke(Path.GetFileName(_lyrics.SourcePath));
                var translatedLineCount = _lyrics.Lines.Count(
                    line => !string.IsNullOrWhiteSpace(line.Translation));
                AppLogger.Log(
                    $"Loaded lyrics: {_lyrics.SourcePath}; " +
                    $"translation: {_lyrics.TranslationPath ?? "embedded/none"}; " +
                    $"translated lines: {translatedLineCount}/{_lyrics.Lines.Count}");
                if (_lyrics.TranslationError is not null) AppLogger.Log(_lyrics.TranslationError);
            }
        }
        catch (Exception error)
        {
            _lyrics = null;
            _lineIndex = null;
            StatusChanged?.Invoke($"歌词读取失败：{error.Message}");
            AppLogger.Log($"Unable to load lyrics for {fullPath}: {error}");
        }
        DisplayChanged?.Invoke();
    }

    private void HandlePosition(string payload)
    {
        if (!double.TryParse(payload.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return;
        _positionMs = checked((long)Math.Round(seconds * 1000d));
        var next = _lyrics?.FindLineIndex(_positionMs);
        if (next != _lineIndex) _lineIndex = next;
        DisplayChanged?.Invoke();
    }

    private void ClearLyrics()
    {
        _currentMedia = null;
        _lyrics = null;
        _lineIndex = null;
        _positionMs = 0;
        DisplayChanged?.Invoke();
    }

    private void ClearCurrentLine()
    {
        _lineIndex = null;
        DisplayChanged?.Invoke();
    }

    private string? LocatePlayer(string? explicitPath)
    {
        foreach (var candidate in EnumeratePlayerCandidates(explicitPath))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
                var fullPath = Path.GetFullPath(expanded);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch (Exception error) when (
                error is ArgumentException
                    or NotSupportedException
                    or PathTooLongException
                    or System.Security.SecurityException)
            {
                AppLogger.Log($"Ignoring invalid MPC-HC candidate '{candidate}': {error.Message}");
            }
        }

        return null;
    }

    private IEnumerable<string?> EnumeratePlayerCandidates(string? explicitPath)
    {
        yield return explicitPath;
        yield return _settings.PlayerPath;

        foreach (var directory in new[]
                 {
                     AppContext.BaseDirectory,
                     Path.Combine(AppContext.BaseDirectory, ".."),
                 })
        {
            yield return Path.Combine(directory, "mpc-hc64.exe");
            yield return Path.Combine(directory, "mpc-hc.exe");
        }

        foreach (var registeredPath in ReadRegisteredPlayerPaths())
            yield return registeredPath;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in DistinctDirectories(
                     programFiles,
                     programFilesX86,
                     Environment.GetEnvironmentVariable("ProgramW6432")))
        {
            yield return Path.Combine(root, "MPC-HC", "mpc-hc64.exe");
            yield return Path.Combine(root, "MPC-HC", "mpc-hc.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var relativePath in new[]
                 {
                     Path.Combine("Programs", "MPC-HC", "mpc-hc64.exe"),
                     Path.Combine("Programs", "MPC-HC", "mpc-hc.exe"),
                     Path.Combine("MPC-HC", "mpc-hc64.exe"),
                     Path.Combine("MPC-HC", "mpc-hc.exe"),
                     Path.Combine("Microsoft", "WinGet", "Links", "mpc-hc64.exe"),
                     Path.Combine("Microsoft", "WinGet", "Links", "mpc-hc.exe"),
                 })
        {
            yield return Path.Combine(localAppData, relativePath);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var scoopRoot in DistinctDirectories(
                     Environment.GetEnvironmentVariable("SCOOP"),
                     Path.Combine(userProfile, "scoop"),
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "scoop")))
        {
            yield return Path.Combine(scoopRoot, "apps", "mpc-hc", "current", "mpc-hc64.exe");
            yield return Path.Combine(scoopRoot, "apps", "mpc-hc", "current", "mpc-hc.exe");
        }

        foreach (var chocolateyRoot in DistinctDirectories(
                     Environment.GetEnvironmentVariable("ChocolateyInstall"),
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                         "chocolatey")))
        {
            yield return Path.Combine(chocolateyRoot, "bin", "mpc-hc64.exe");
            yield return Path.Combine(chocolateyRoot, "bin", "mpc-hc.exe");
        }

        foreach (var pathDirectory in DistinctDirectories(
                     (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            yield return Path.Combine(pathDirectory, "mpc-hc64.exe");
            yield return Path.Combine(pathDirectory, "mpc-hc.exe");
        }
    }

    private static IEnumerable<string> ReadRegisteredPlayerPaths()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var executableName in new[] { "mpc-hc64.exe", "mpc-hc.exe" })
                {
                    var path = ReadRegisteredPlayerPath(hive, view, executableName);
                    if (!string.IsNullOrWhiteSpace(path)) yield return path;
                }
            }
        }
    }

    private static string? ReadRegisteredPlayerPath(
        RegistryHive hive,
        RegistryView view,
        string executableName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appPathKey = baseKey.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            return appPathKey?.GetValue(null) as string;
        }
        catch (Exception error) when (
            error is UnauthorizedAccessException
                or IOException
                or System.Security.SecurityException)
        {
            AppLogger.Log(
                $"Unable to read MPC-HC App Paths entry ({hive}, {view}, {executableName}): " +
                error.Message);
            return null;
        }
    }

    private static IEnumerable<string> DistinctDirectories(params string?[] directories) =>
        directories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => directory!.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    internal static void ExerciseForSmokeTest()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mpc-lyrics-player-discovery-{Guid.NewGuid():N}");
        var configuredPlayer = Path.Combine(temporaryDirectory, "mpc-hc64.exe");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllBytes(configuredPlayer, Array.Empty<byte>());
            var settings = AppSettings.Default();
            settings.PlayerPath = configuredPlayer;
            var controller = new MpcController(0, settings);
            var resolved = controller.LocatePlayer(null);
            if (!string.Equals(
                    resolved,
                    Path.GetFullPath(configuredPlayer),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Configured MPC-HC path was not resolved: {resolved ?? "<null>"}");
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private void SendCommand(nint target, nuint command, string payload)
    {
        var units = (payload + '\0').ToCharArray();
        var pin = GCHandle.Alloc(units, GCHandleType.Pinned);
        try
        {
            var data = new NativeMethods.COPYDATASTRUCT
            {
                dwData = command,
                cbData = checked((uint)(units.Length * sizeof(char))),
                lpData = pin.AddrOfPinnedObject(),
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(data, pointer, false);
                NativeMethods.SendMessageW(target, NativeMethods.WM_COPYDATA, (nuint)_receiverHwnd, pointer);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    private static bool TryParseHwnd(string text, out nint hwnd)
    {
        hwnd = 0;
        var trimmed = text.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
        {
            hwnd = new nint(decimalValue);
            return hwnd != 0;
        }
        var hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
        if (long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexValue))
        {
            hwnd = new nint(hexValue);
            return hwnd != 0;
        }
        return false;
    }

    private static List<string> SplitMpcFields(string input)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var index = 0; index < input.Length; index++)
        {
            var character = input[index];
            if (character == '\\' && index + 1 < input.Length && input[index + 1] == '|')
            {
                current.Append('|');
                index++;
            }
            else if (character == '|')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
