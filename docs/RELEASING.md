# 发布流程

当前仅支持 Windows。macOS/Linux 没有移植完成，不提供这些平台的安装包或便携包。

1. 同步修改 `VERSION`、`src/AssemblyInfo.cs` 和 `docs/RELEASE-NOTES.md` 的版本号。
2. 在 Windows 项目根目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。
3. 运行 `dist\QuickLauncher.exe --self-test` 并确认 `tests\work\result.txt` 为 PASS。
4. 安装官方 Inno Setup 6（仅构建安装包需要），运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\package-windows.ps1`。
5. 便携 ZIP 和安装 EXE 位于 `dist\release\`。只生成 ZIP 时可加 `-PortableOnly`，Release 仅上传这两类包。
6. 检查 `git status` 和 `git diff --cached`，确保不包含个人列表、备份、测试输出、工具或本机私有路径。
7. 将变更提交到 `main`，推送后确认 GitHub Actions 构建通过。
8. 创建与 `VERSION` 对应的标签（例如 `v1.0.0`）并推送。Actions 验证版本后发布 GitHub Release。
9. 从 Release 下载两个包，验证安装、卸载和便携数据存储。

Actions 额外运行 `tests/Test-Updater.ps1`，在临时环境中从低版本升级，验证便携替换、安装升级、等待进程退出、自动重启、语言和列表保留。此脚本会安装软件，只能在临时 CI 环境运行。

一键更新从本仓库 Release 下载与当前版本类型对应的包，使用 GitHub 附件页面公布的 SHA-256 校验值，不额外发布校验文件。附件页面格式变化或无法读取校验值时拒绝更新并允许重试。安装包保持 Inno Setup 的固定 AppId，升级目录通过 `/DIR` 指定。

发布包采用显式文件白名单生成；便携包不会复制 `data` 或测试目录，安装器也不包含任何用户列表。
安装版卸载仅删除安装文件和快捷方式，保留 `%LOCALAPPDATA%\QuickLauncher`。
便携版和安装版共享单实例限制，测试另一版本前需先退出正在运行的版本。

仓库使用 MIT 许可证；图标由内置图像工具生成，提示词保存在 `assets/DESIGN.md`。
