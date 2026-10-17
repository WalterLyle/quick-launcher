# 快捷启动器

**简体中文** | [English](README.en.md)

Windows 桌面小工具，把常用软件、文件、文件夹和网站集中起来，一次打开。

当前版本为 **1.0.0**，支持 Windows 10/11，需要系统具备 .NET Framework 4.7 或更高版本。macOS/Linux 尚未移植，本次不提供这两类发行包。

## 下载与安装

在 [Releases 下载页面](https://github.com/WalterLyle/quick-launcher/releases/latest) 下载：

- **安装版** `QuickLauncher-1.0.0-windows-setup.exe`：安装到当前用户目录，无需管理员权限，提供开始菜单、可选桌面快捷方式和卸载入口。
- **便携版** `QuickLauncher-1.0.0-windows-portable.zip`：解压到可写目录，直接运行 `QuickLauncher.exe`，保留旁边的 `portable.flag`。

安装版列表保存在 `%LOCALAPPDATA%\QuickLauncher\items.xml`；便携版列表保存在自身目录的 `data\items.xml`，可随整个目录移动。安装包和便携 ZIP 均不包含个人列表。安装版卸载时保留列表。

两个版本共享单实例限制，运行另一版本前请先退出当前启动器。当前发行包未使用代码签名证书签名。

## 使用

安装后从开始菜单或桌面启动；便携版直接运行解压目录中的 `QuickLauncher.exe`。从源码构建后可运行 `dist\QuickLauncher.exe`。

左下角的“Switch to English / 切换到中文”按钮可即时切换界面语言，无需重启，语言偏好会随列表配置保存。切换保留列表、选中项和滚动位置；系统文件选择器的内置控件遵循 Windows 的显示语言。

点击“全部打开”或“全部打开并退出”时，已运行的软件会跳过，不再重复启动；支持直接添加的 `.exe` 和指向 `.exe` 的 `.lnk` 快捷方式，按程序完整路径匹配。普通文件和文件夹仍正常打开。跳过快捷方式时不执行其参数；需要打开的软件仍把原快捷方式交给 Windows Shell，保留参数和工作目录。无法读取目标进程路径时会按原逻辑打开。

快捷启动器只能运行一个实例。已经打开时，再次双击程序或其快捷方式会尝试显示已有窗口，不会新增窗口。

发现新版本时，右上角会显示“更新”按钮，点击进入下载页面。仅在每次启动时后台检查一次，检查失败不会打断使用。

1. 将 `.lnk` 快捷方式、文件或文件夹从资源管理器拖到列表里，也可以使用“添加文件 / 添加文件夹”。
   网站可点击“添加网站”，直接输入网址（例如 `www.example.com`，省略协议时自动补全 `https://`），无需创建本地快捷方式文件。网站与文件一起自动保存，类型显示为“网站”；通过 Windows Shell 交给默认浏览器打开。浏览器已运行时仍会发送网址。
2. 点击“全部打开”，逐一向 Windows 发出打开请求，启动器保留。
3. 点击“全部打开并退出本软件（Space）”，或在主界面按空格，发出所有请求后退出启动器；已经打开的应用和文件继续运行。

列表自动保存，重启程序后恢复；同一路径不会重复添加。选中项目后点击“移除选中”或按 Delete 移除；“清空列表”只清除记录。

若有文件失效、没有默认应用或系统拒绝启动，会提示具体项目，并保留界面。快捷方式的参数、工作目录由 Windows 处理。打开请求成功不代表目标程序已完成加载。

以普通权限运行即可；如果以管理员权限运行，Windows 通常阻止从普通资源管理器拖入文件。

安装版配置位置：`%LOCALAPPDATA%\QuickLauncher\items.xml`，上一次配置备份为 `items.xml.bak`。便携版对应自身目录的 `data\items.xml` 和 `items.xml.bak`；也可以通过 `--portable` 参数启用便携模式。

## 开发

无需安装 Node、Python 或 .NET SDK。使用 Windows 自带 .NET Framework 4.x 编译器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$p = Start-Process .\dist\QuickLauncher.exe -ArgumentList '--self-test' -Wait -PassThru
Get-Content .\tests\work\result.txt
```

`src\Program.cs` 包含界面、列表存储与 Shell 启动逻辑；`tests\LauncherTests.cs` 包含验证程序。

当前 2D 小松鼠图标位于 `assets\QuickLauncher-dynamic.ico`，透明原图和生成提示词保存在 `assets\squirrel-dynamic.png` 与 `assets\DESIGN.md`。`build.ps1` 将图标嵌入程序和窗口；修改原图后可运行 `build-icon.ps1` 重建多尺寸 ICO，再重新编译。

## 打包与发布

本地运行 `package-windows.ps1` 生成 Windows 安装包和便携 ZIP；安装包构建另需官方 Inno Setup 6，程序运行时没有第三方依赖。仅打便携包时可运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\package-windows.ps1 -PortableOnly`。

GitHub Actions 会在提交和 PR 时构建、验证并生成包；推送与 `VERSION` 匹配的版本标签时自动发布 Release。具体步骤见 [发布流程](docs/RELEASING.md)。

## 隐私与许可证

程序仅保存你添加的路径和网址，不复制、移动或删除原文件。源码中没有遥测或列表上传逻辑；更新检查仅访问本项目的 GitHub Releases 接口，不上传启动列表。打开网站时由默认浏览器访问对应网站。

`.gitignore` 排除了个人列表、测试产物、构建包、下载工具与常见凭据文件。发布包按文件白名单打包，不包含开发者的个人列表。

项目使用 [MIT 许可证](LICENSE)。

界面内置开源字体[得意黑 Smiley Sans](https://github.com/atelier-anchor/smiley-sans)，无需另外安装。字体采用 [SIL Open Font License 1.1](assets/fonts/OFL.txt)，随安装版和便携版附带字体许可证。
