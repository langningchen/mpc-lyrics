# Contributing

## 开发环境

- Windows 10 2004 或更高版本；
- x64 MPC-HC；
- WSL 2，且启用了 Windows 互操作；
- .NET 10 SDK。首次运行构建脚本时，也可以让脚本安装独立 SDK 到 `%LOCALAPPDATA%`。

## 提交前检查

在仓库根目录运行：

```bash
./build-wsl.sh
```

只有源码审计、Release 发布和 Windows WinUI 冒烟测试全部通过，构建才会成功。修改字幕解析时，请同步扩充 `SubtitleLoader.ExerciseForSmokeTest()`；修改渲染布局时，请扩充 `OverlayRenderer.ExerciseForSmokeTest()`。

## 代码约定

- 所有源码使用无 BOM UTF-8；
- 遵循 `.editorconfig`；
- UI 控件优先在 XAML 中声明，不在运行时重建 WinUI 原生控件；
- Win32 互操作集中放在 `Native/`；
- 配置新增字段必须提供默认值，并在 `AppSettings.Normalize()` 与重置逻辑中处理；
- 提交代码即表示同意按 `AGPL-3.0-only` 许可分发该贡献；
- 不提交 `dist/`、`bin/`、`obj/` 或用户本地配置。

## 发布

1. 更新 `MpcLyrics.csproj` 中的版本；
2. 在 `CHANGELOG.md` 顶部记录用户可见变更；
3. 运行 `./build-wsl.sh`；
4. 在一台没有开发环境的 Windows 机器上解压并完成 MPC-HC 联动检查；
5. 推送与项目版本一致的 `v*` 标签，由 GitHub Actions 自动构建压缩包并创建 Release。

```bash
git tag v1.6.0
git push origin v1.6.0
```
