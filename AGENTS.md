# 快捷启动器

这是 Windows 原生桌面项目。构建和验证命令均在项目根目录执行。
后续开发对话应在 Codex 的此项目下新建，优先使用中文交流。

技术栈：C#、WinForms、Windows 自带的 .NET Framework 4.x 编译器，无第三方依赖。
编译：powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
验证：运行 dist\QuickLauncher.exe --self-test；测试结果为 tests\work\result.txt。

核心要求：拖入快捷方式或文件；按钮全部打开；按钮全部打开并退出启动器。
快捷方式必须交给 Windows Shell 打开，保留其目标、参数与工作目录。
文件用默认应用打开。添加项目只保存路径，不复制、移动或删除用户文件。
打开失败逐项提示，其他项目仍继续打开；存在失败时保留界面。
列表保存在 %LOCALAPPDATA%\QuickLauncher\items.xml，修改后自动保存。
便携版（portable.flag 或 --portable）将列表保存在程序目录的 data\items.xml；安装和卸载不得删除个人列表。
禁止把用户个人启动列表提交到仓库。

发布：当前仅发布 Windows 版；Mac/Linux 留待后续移植。package-windows.ps1 生成便携 ZIP 和 Inno Setup 安装包，流程见 docs/RELEASING.md。打包仅使用显式文件白名单，不发布单独校验文件。
