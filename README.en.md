# QuickLauncher

[简体中文](README.md) | **English**

A lightweight Windows desktop utility that opens your applications, files, folders, and websites together.

Version **1.0.0** supports Windows 10/11 with .NET Framework 4.x. macOS and Linux ports are not available yet. The application interface is currently in Simplified Chinese; this page provides English documentation.

## Download and installation

Download from the [Releases page](https://github.com/WalterLyle/quick-launcher/releases/latest):

- **Installer:** `QuickLauncher-1.0.0-windows-setup.exe`. Installs for the current user without administrator access and provides a Start menu entry, an optional desktop shortcut, and an uninstaller.
- **Portable:** `QuickLauncher-1.0.0-windows-portable.zip`. Extract to a writable folder and run `QuickLauncher.exe`. Keep `portable.flag` beside the executable.
- **Checksums:** `SHA256SUMS.txt`. Use PowerShell's `Get-FileHash -Algorithm SHA256` to calculate and compare download hashes.

The installed edition stores your list in `%LOCALAPPDATA%\QuickLauncher\items.xml`. The portable edition stores it in `data\items.xml` inside its own folder, so you can move your list by copying the entire folder. Neither download includes a personal launch list. Uninstalling the installed edition preserves your list.

Both editions share a single-instance restriction: exit the running launcher before starting another edition. The current release is not code-signed.

## Usage

Start the installed edition from the Start menu or desktop. For the portable edition, run `QuickLauncher.exe` from the extracted folder. After building from source, run `dist\QuickLauncher.exe`.

1. Drag `.lnk` shortcuts, files, or folders from File Explorer into the list, or use **添加文件** (Add files) / **添加文件夹** (Add folder). To add a website, click **添加网站** (Add website) and enter its address, such as `www.example.com`. Addresses without a scheme use `https://`. No local shortcut file is created. Websites are saved with the other entries and opened through Windows Shell in your default browser, even if the browser is already running.
2. Click **全部打开** (Open all) to send an open request for each entry and keep the launcher open.
3. Click **全部打开并退出** (Open all and exit) to open all entries and close the launcher when all requests succeed. Launched applications and documents remain open.

Running applications are skipped when they can be detected. This supports `.exe` entries and `.lnk` shortcuts targeting an `.exe`, matched by the full executable path. A skipped shortcut's arguments are not executed. If a process path cannot be read, the entry is opened as usual. Websites, files, and folders are opened normally; the launcher does not detect whether a browser tab, document, or folder window is already open.

Windows Shell handles shortcuts, preserving their targets, arguments, and working directories. Files use their default applications. A successful open request does not mean the target application has finished loading.

The list is saved automatically and restored on startup. Duplicate file paths are not added. Select entries and click **移除选中** (Remove selected), or press Delete, to remove them. **清空列表** (Clear list) only removes the saved entries; original files remain untouched. Both removal buttons are on the right side of the toolbar.

If an entry cannot be opened, the launcher continues with the remaining entries, reports the failures, and stays open. Only one launcher instance can run per Windows session; starting it again attempts to show the existing window.

Run with normal user permissions. Running as administrator can prevent drag-and-drop from a normal File Explorer window.

Configuration and backup locations:

- Installed: `%LOCALAPPDATA%\QuickLauncher\items.xml` and `items.xml.bak`.
- Portable: `data\items.xml` and `data\items.xml.bak` inside the application folder. You can also enable portable mode with `--portable`.

## Development

The application uses C#, WinForms, and the .NET Framework 4.x compiler included with Windows. No Node.js, Python, .NET SDK, or third-party runtime dependencies are needed.

Run these commands from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$p = Start-Process .\dist\QuickLauncher.exe -ArgumentList '--self-test' -Wait -PassThru
Get-Content .\tests\work\result.txt
```

`src\Program.cs` contains the interface, list storage, and Shell launch logic. `tests\LauncherTests.cs` contains the self-tests.

The current 2D squirrel icon is `assets\QuickLauncher-flat-1.ico`. Its transparent source image is `assets\squirrel-flat-1.png`, and its generation prompts are recorded in `assets\DESIGN.md`. The build embeds the icon in the executable and window resources. After changing the source image, run `build-icon.ps1` to regenerate the multi-size ICO, then rebuild.

## Packaging and releases

Run `package-windows.ps1` locally to generate the Windows installer, portable ZIP, and SHA-256 checksums. Building the installer requires the official Inno Setup 6 compiler; the application itself has no third-party runtime dependencies. To build only the portable ZIP:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package-windows.ps1 -PortableOnly
```

GitHub Actions builds, tests, and packages commits to `main` and pull requests. Pushing a version tag matching `VERSION` publishes a GitHub Release after validation. See the [release procedure](docs/RELEASING.md) (Chinese).

## Privacy and license

QuickLauncher stores only the paths and URLs you add. It does not copy, move, or delete the original files. The source contains no telemetry or launch-list upload logic. Opening a website asks your default browser to visit that website.

`.gitignore` excludes personal lists, test output, build packages, downloaded tools, and common credential files. Release packages use an explicit file allowlist and do not include the developer's personal launch list.

This project is licensed under the [MIT License](LICENSE).
