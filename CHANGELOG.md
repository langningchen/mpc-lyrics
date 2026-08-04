# Changelog

## 1.6.0

- Removed software inverse colors from text, outlines, backgrounds, and settings; legacy inverse selections now migrate to custom colors.
- Replaced screen capture and CPU blur with a separate DWM `DWMSBT_TRANSIENTWINDOW` Desktop Acrylic layer on Windows 11 22H2 and later.
- Kept the system acrylic layer synchronized behind the lyrics overlay during show, hide, topmost changes, moves, and resizes.
- Removed the arbitrary blur-radius setting because Windows system acrylic owns its material recipe; unsupported Windows versions hide the acrylic option.
- Deleted all `BitBlt` backdrop capture, per-pixel inversion, software blur, recursive effect feedback, and related bitmap caches.
- Added a Windows runtime check for the actual DWM backdrop type and foreground/background window bounds.

## 1.5.0

- Added an independent “在空的时候隐藏” option to current text, current translation, next text, and next translation while retaining fixed empty slots by default.
- Collapsed only the empty tracks that opt into hiding, including allowing the final current line to use the full overlay when both empty next tracks are configured to hide.
- Reused desktop captures and processed inverse/acrylic bitmaps across nearby lyric frames instead of capturing, inverting, and blurring the full layered-window surface every 100 ms.
- Skipped full layered-window rendering and upload between line changes when the current text fits its viewport, while retaining position-driven updates for overflowing scrolling lyrics.
- Paused MPC position polling during interactive overlay moves/resizes and bypassed global mouse-hook marshaling for all non-middle-button mouse traffic.
- Limited inverse backdrop capture to tracks that currently contain visible text and added smoke coverage for empty-slot collapse and effect-cache reuse.

## 1.4.2

- Removed the native `WS_MAXIMIZEBOX` and resizing frame from the standard settings window, forcing Windows to disable the maximize caption button.
- Rejected maximize system commands and caption double-clicks at the HWND message layer while preserving standard title-bar dragging, minimizing, and closing.
- Added a Windows runtime check that inspects the real HWND styles and verifies that a system maximize command cannot change the window state or bounds.

## 1.4.1

- Ignored empty timestamped LRC clear markers when building display lines, preventing them from consuming preview slots, stealing nearby translations, or changing odd/even placement.
- Kept each non-final subtitle active through the gap before the next real line so current/next prediction remains stable.
- Added real-file-shaped bilingual LRC regression coverage and translated-line counts to the runtime log.

## 1.4.0

- Added custom, Windows system-accent, and per-pixel inverse color sources for every lyric fill, outline, and solid background color.
- Preserved an independent opacity for dynamic colors and disabled RGB editing while system-accent or inverse mode is selected.
- Added an adjustable software acrylic blur layer that can be combined with either the existing color background or image background.
- Captured the real desktop area beneath the layered lyrics window for inverse and acrylic rendering while excluding the overlay itself from feedback.
- Limited backdrop capture to active acrylic and visible inverse-color content, leaving the normal rendering path unchanged.
- Kept current/next original and translation tracks in four stable slots, including empty next-line placeholders at the end of the subtitle timeline.
- Expanded source and Windows runtime smoke tests for inversion, accent opacity, acrylic composition, native backdrop capture, and dynamic color controls.

## 1.3.0

- Replaced the custom settings-window chrome and drag handling with a standard Windows title bar while explicitly disabling resizing and maximizing.
- Kept the lyrics overlay lock state unchanged when settings opens, and grouped the home controls into five category flyouts.
- Added optional odd/even line position swapping so a previewed lyric remains in the same physical area when it becomes the current line.
- Removed the no-lyrics placeholder; a locked overlay with no current or next lyric now stays hidden, while an unlocked edit surface remains blank.
- Applied dark DWM border, caption, and text colors to the standard window to eliminate the light top edge.

## 1.2.1

- Changed the project license from MIT to GNU Affero General Public License v3.0 only (`AGPL-3.0-only`).
- Expanded MPC-HC discovery to cover App Paths registry entries, both Program Files roots, LocalAppData and WinGet links, Scoop, Chocolatey, and directories listed in `PATH`.
- Added a Windows smoke check for configured player-path resolution and improved startup diagnostics by logging the selected MPC-HC executable.

## 1.2.0

- Added independent current-original, current-translation, next-original, and next-translation tracks, each with its own visibility switch and text styling.
- Added next-line preview placement on the left, top, right, or bottom of the current line.
- Centered the settings window on the lyrics display whenever it opens and added dragging from its header area.
- Removed the DWM activation border color and corrected the native rounded region so the settings window no longer leaves a light top edge.
- Stopped layered-bitmap rendering during interactive move/resize operations and removed the duplicate timer render that made dragging sluggish.
- Extracted the repeated text-style UI into a reusable declarative WinUI control and expanded the Windows smoke test to cover all five flyouts and all next-line layouts.
- Added repository metadata, `.editorconfig`, contribution guidance, and a complete end-user README for release publishing.

## 1.1.2

- Restored the standard WinUI-generated application entry point instead of replacing it with `DISABLE_XAML_GENERATED_MAIN` and a hand-written `Application.Start` loop.
- Raised the minimum Windows target to Windows 10 2004 and enabled undocked reg-free WinRT activation required by the self-contained unpackaged runtime.
- WSL builds now smoke-test and retain a runnable copy under Windows LocalAppData; the repository `dist` directory remains the portable package to copy onto a Windows-local disk before launch.
- Pinned the UI runtime to the supported Windows App SDK 1.8.10 servicing line and the matching stable 26100 SDK build tools instead of the newly changed 2.3.1 startup/tooling path.
- Moved Mica assignment out of XAML and into code after `InitializeComponent()`.
- Replaced the full settings page with a borderless editor popup positioned beside the lyrics overlay, containing two switches and three anchored editor flyouts.
- Added live original/translation font size, outline width, bold, italic, alignment, outline-color, and fill-color editing.
- Unconstrained all three flyouts from the compact root window, widened the two color-editor layouts, and replaced text glyphs/cycling alignment with standard icon toggle groups.
- Moved lock and always-on-top toggles into the same compact row as the three editor entry buttons.
- Added solid-color or image lyrics backgrounds with file picking, crop/fit/stretch modes, opacity, and cached real-time rendering.
- Added the MIT-licensed `SubtitlesParser` while retaining the enhanced-LRC parser, enabling SRT, VTT, ASS/SSA, SUB/SBV, TTML/DFXP/XML, encoding detection, and companion translation subtitle matching.
- Fixed folder publishing to retain the application PRI plus application/page XBF files required by `ms-appx` resource resolution.
- Added a Windows runtime smoke test to `build-wsl.sh`; a build now fails unless the popup, all three flyouts, and all five native color pickers can be created and realized successfully.
- Narrowed the editor home popup, aligned the editor headings and color controls, and removed the remaining light system border from the popup.
- Added a visible edit-mode boundary plus reliable whole-surface dragging and edge/corner resizing for the layered lyrics overlay.
- Changed a normal no-argument launch to start or foreground MPC-HC with subtitle integration; `--settings` remains available for opening only the editor.
- Removed the loaded-status prefix and added smooth looping for subtitle filenames wider than the compact status viewport.
- Replaced the overlapping WinUI/DWM corner treatments with one DPI-aware native rounded region and clipped the outer activation edge, removing the black outer arc and light top border.

## 1.1.1

- 修复 `audit-source.ps1` 在 Windows PowerShell 5.1 下使用系统 ANSI 编码读取 UTF-8 XAML，导致中文乱码并误报 XML 不完整的问题。
- 所有源码审计读取改为严格 UTF-8；无效 UTF-8 会给出明确文件名。
- 增加 Unicode 替换字符检查，避免损坏文本进入构建。


## 1.1.0

- 以原始稳定核心为基础重新编写设置窗口，不继续叠加 1.0.6–1.0.7.x 的动态 UI 补丁。
- 删除纯 C# 动态视觉树、自制 `ColorSettingRow` 和 `RgbaColorEditor`。
- 新增标准 `SettingsWindow.xaml` 与 `SettingsWindow.xaml.cs`。
- 使用 WinUI 3 原生 `NavigationView`；首项直接在 XAML 中选中，不再在构造阶段调用 `SelectedItem`。
- 只实例化一个 WinUI 3 原生 `ColorPicker`，常驻颜色页面并负责五项颜色；不使用 `ContentDialog`、`Flyout`、`Popup`、`Expander` 或运行时动态创建。
- 使用 XAML `MicaBackdrop Kind="BaseAlt"`、系统标题栏和 WinUI 主题资源，减少窗口生命周期及标题栏互操作代码。
- 将实时预览固定为 160 像素并裁切，字号变化不会改变页面布局或滚动位置。
- 保留歌词解析、双语歌词、MPC-HC 联动、透明悬浮窗、点击穿透、中键打开设置、拖动缩放、置顶和配置保存功能。
- 构建前审计检查 XAML 可解析性、事件处理器、重复 `x:Name`、原生控件数量、密封控件继承和动态重建官方控件等问题。
