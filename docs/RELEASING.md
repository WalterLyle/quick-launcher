# 发布流程

当前仅支持 Windows。macOS/Linux 没有移植完成，不提供这些平台的安装包或便携包。

1. 同步修改 `VERSION`、`src/AssemblyInfo.cs` 和 `docs/RELEASE-NOTES.md` 的版本号。
2. 在 Windows 项目根目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`。
3. 运行 `dist\QuickLauncher.exe --self-test` 并确认 `tests\work\result.txt` 为 PASS。
4. 安装官方 Inno Setup 6（仅构建安装包需要），运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\package-windows.ps1`。
5. 便携 ZIP、安装 EXE 和 `SHA256SUMS.txt` 位于 `dist\release\`。只生成 ZIP 时可加 `-PortableOnly`。
6. 检查 `git status` 和 `git diff --cached`，确保不包含个人列表、备份、测试输出、工具或本机私有路径。
7. 将变更提交到 `main`，推送后确认 GitHub Actions 构建通过。
8. 创建与 `VERSION` 对应的标签（例如 `v1.0.0`）并推送。Actions 验证版本后发布 GitHub Release。
9. 从 Release 下载两个包，核对 SHA-256，验证安装、卸载和便携数据存储。

发布包采用显式文件白名单生成；便携包不会复制 `data` 或测试目录，安装器也不包含任何用户列表。
安装版卸载仅删除安装文件和快捷方式，保留 `%LOCALAPPDATA%\QuickLauncher`。
便携版和安装版共享单实例限制，测试另一版本前需先退出正在运行的版本。

仓库使用 MIT 许可证；图标由内置图像工具生成，提示词保存在 `assets/DESIGN.md`。
