# MPC Lyrics

[![Build](https://github.com/langningchen/mpc-lyrics/actions/workflows/build.yml/badge.svg)](https://github.com/langningchen/mpc-lyrics/actions/workflows/build.yml)

MPC Lyrics 是一个面向 Windows 和 MPC-HC 的轻量桌面歌词悬浮工具。它会读取媒体文件旁的歌词或字幕，根据 MPC-HC 的播放进度显示本句文字、翻译以及可提前预览的下一句。

## 功能

- MPC-HC 播放进度同步；
- 本句文字、本句翻译、下句文字、下句翻译四个独立显示项；
- 四个显示区域默认按已开启的项目固定占位；每项也可选择“在空的时候隐藏”，按需释放空区域；
- 每个显示项都可单独开关，并可设置空内容行为、字号、描边、粗体、斜体、对齐和颜色；
- 文字填充、描边和纯色背景都支持自定义颜色或 Windows 系统主题色，并可独立设置透明度；
- 下一句可放在本句的左侧、上方、右侧或下方，并可在奇偶句自动交换位置；
- 支持透明色、纯色或图片背景；Windows 11 22H2 及以上还可叠加由 DWM 绘制的系统亚克力；
- 歌词窗口可置顶、点击穿透、拖动和缩放；
- 支持普通/增强 LRC、SRT、WebVTT、ASS/SSA、SUB/SBV 和 TTML/DFXP/XML；
- 支持 UTF-8、UTF-16 和常见 GBK 字幕文件。

## 系统要求

- Windows 10 2004 或更高版本，x64；
- MPC-HC x64；
- 发布包已经包含 .NET 和 Windows App SDK 运行时，无需另外安装。

## 使用说明

### 1. 安装

从仓库的 Releases 下载发布压缩包并完整解压到 Windows 本地磁盘。程序采用文件夹式 self-contained 发布，不能只单独复制 `MpcLyrics.exe`。

如果没有单独指定播放器，程序会依次查找：

1. 命令行 `--player` 指定的路径；
2. 配置文件中的 `PlayerPath`；
3. 与 `MpcLyrics.exe` 相同或上一级目录中的便携版；
4. 当前用户和本机注册表的 `App Paths`；
5. `%ProgramFiles%\MPC-HC` 和 `%ProgramFiles(x86)%\MPC-HC`；
6. `%LOCALAPPDATA%\Programs\MPC-HC`、`%LOCALAPPDATA%\MPC-HC` 和 WinGet Links；
7. Scoop、全局 Scoop 和 Chocolatey 的默认目录；
8. `PATH` 环境变量中的 `mpc-hc64.exe` 或 `mpc-hc.exe`。

### 2. 准备字幕

把字幕放在媒体文件旁并使用相同主文件名。例如：

```text
Song.flac
Song.lrc
Song.trans.lrc
```

或：

```text
Movie.mkv
Movie.srt
Movie.zh.srt
```

双行字幕会自动拆分为文字和翻译。独立翻译文件可使用 `trans`、`translation`、`zh`、`zh-CN`、`cn`、`chs`、`中文` 或 `翻译` 等后缀；时间轴在 750 毫秒内的字幕会自动匹配。

LRC 中只有时间戳、正文为空的行会被视为清屏标记，不参与本句/下句计数、翻译匹配或奇偶位置计算。非末句会持续到下一条有效歌词开始，避免间奏空档让预测突然消失。

### 3. 启动

直接运行 `MpcLyrics.exe` 会启动 MPC-HC 并建立同步。也可以把媒体文件传给程序：

```powershell
.\MpcLyrics.exe "D:\Music\Song.flac"
```

显式指定播放器：

```powershell
.\MpcLyrics.exe --player "D:\Apps\MPC-HC\mpc-hc64.exe" "D:\Music\Song.flac"
```

只打开设置窗口：

```powershell
.\MpcLyrics.exe --settings
```

### 4. 调整显示

在歌词窗口内单击鼠标中键打开设置。设置窗口每次会在歌词所在屏幕的工作区中央打开；它使用标准 Windows 标题栏，可由系统负责拖动，但不能最大化或改变大小。打开设置不会自动解除歌词窗口的锁定状态。

- `窗口`：控制歌词窗口锁定/鼠标穿透和始终置顶。
- `字幕`：进入 `文字`、`翻译`、`下句文字`、`下句翻译` 四个样式编辑器；每项都可单独决定是否显示，以及内容为空时是保留位置还是隐藏区域。填充色和描边色可选择 `自定义` 或 `系统主题色`。
- `布局`：设置奇数句中下一句位于本句的左侧、上方、右侧或下方；启用“奇偶句交换位置”后，偶数句会使用相反方向，让歌词从预览升级为本句时留在同一物理位置。
- `背景`：选择纯色或图片背景及透明度；在支持的 Windows 11 系统上还可开启系统亚克力，并与半透明颜色或图片叠加。
- `更多`：恢复默认设置或退出程序。

未锁定时，拖动歌词区域可以移动窗口，拖动四边或四角可以缩放。锁定后歌词窗口会点击穿透，但中键仍可打开设置。

当前曲目没有歌词、尚未进入第一句或已经播放完最后一句时，歌词窗口保持空白，不显示占位提示。

四个字幕项默认按开关状态分配固定区域。四项全部开启且当前歌词包含翻译时，会同时显示本句文字、本句翻译、下句文字和下句翻译；播放到最后一句后，下句的两个区域保持为空并继续占位，本句不会因此突然放大或移动。为某项开启“在空的时候隐藏”后，只有该项内容为空时才会释放其位置；例如同时为两个下句项开启后，末句会使用完整区域。

`系统主题色`会自动读取 Windows 当前的强调色，同时保留该颜色项自己的透明度。系统亚克力使用 Windows 11 22H2（内部版本 22621）开始提供的 DWM Desktop Acrylic，不抓取屏幕，也不在 CPU 上执行模糊；较旧系统会隐藏该选项。软件反色、软件模糊以及相关底图捕获已经删除。静止且无需滚动的字幕不会重复上传整窗位图，移动或缩放期间也会暂停歌词轮询。

## 配置和日志

程序会自动保存配置：

```text
%LOCALAPPDATA%\mpc-lyrics\settings.json
```

日志位置：

```text
%LOCALAPPDATA%\mpc-lyrics\startup.log
%LOCALAPPDATA%\mpc-lyrics\crash.log
%LOCALAPPDATA%\mpc-lyrics\diagnostic.txt
```

在 WSL 中运行诊断：

```bash
./run-diagnostic-wsl.sh
```

## 构建

项目使用 .NET 10、Windows App SDK 1.8 和 WinUI 3。推荐从 WSL 调用仓库自带的 Windows 构建脚本：

```bash
chmod +x build-wsl.sh
./build-wsl.sh
```

构建产物位于 `dist/`，其中会包含 AGPL `LICENSE`。脚本会执行以下检查：

- 严格 UTF-8 和 XAML 源码审计；
- Release x64 self-contained 发布；
- XBF/PRI 发布资源完整性检查；
- Windows 端标准设置窗口、九个 Flyout、四项空内容开关、颜色来源与透明度、DWM 系统亚克力后层、四种下一句布局、奇偶换位和空歌词冒烟测试。

由于无注册 WinUI 运行时不适合直接从 `\\wsl.localhost` UNC 路径启动，脚本还会生成一个用于本机测试的 NTFS 副本：

```text
%LOCALAPPDATA%\MpcLyricsCSharpBuild\run\MpcLyrics.exe
```

GitHub Actions 会在推送到 `main`、创建 Pull Request 或手动触发时执行同一套构建，并将完整的 Windows x64 便携目录保留为 14 天的构建产物。推送形如 `v1.6.0` 的标签会自动重新验证源码、生成 `MpcLyrics-v1.6.0-win-x64.zip`，并创建带自动发行说明的 GitHub Release。

```bash
git tag v1.6.0
git push origin v1.6.0
```

## 项目结构

```text
src/MpcLyrics/Core       配置和歌词数据模型
src/MpcLyrics/Services   字幕加载、MPC-HC 通信和位图渲染
src/MpcLyrics/Native     Win32 分层窗口和原生互操作
src/MpcLyrics/UI         WinUI 3 设置窗口与样式编辑器
scripts                  构建、审计和诊断脚本
examples                 示例 LRC 文件
```

开发约定见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

Copyright (C) 2026 MPC Lyrics contributors。

本项目按 [GNU Affero General Public License v3.0](LICENSE) 授权，SPDX 标识为 `AGPL-3.0-only`。程序不提供任何明示或暗示的担保；项目依赖仍分别遵循其各自许可证。
