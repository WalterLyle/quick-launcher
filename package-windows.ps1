param(
    [string]$InnoCompiler = '',
    [switch]$PortableOnly
)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must be a three-part numeric version.' }
$executable = Join-Path $PSScriptRoot 'dist\QuickLauncher.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Run build.ps1 first.' }
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
if ($fileVersion -ne ($version + '.0')) { throw 'VERSION and executable version do not match.' }

# A fresh directory and an explicit file list prevent personal data from entering a package.
$output = Join-Path $PSScriptRoot 'dist\release'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stage = Join-Path $PSScriptRoot ('dist\package-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $stage 'QuickLauncher.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\QuickLauncher.exe.config') -Destination (Join-Path $stage 'QuickLauncher.exe.config')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\fonts\OFL.txt') -Destination (Join-Path $stage 'LICENSE-SmileySans.txt')
[IO.File]::WriteAllText((Join-Path $stage 'portable.flag'), '')
$portableReadme = @'
QuickLauncher portable edition

Extract this entire ZIP to a writable folder and run QuickLauncher.exe.
Keep portable.flag beside the executable. Your launch list will be stored in
data\items.xml in this folder. Copy the whole folder to move your list.
The ZIP contains no saved launch list. Do not redistribute your personal data folder.

Requires Windows 10/11 and .NET Framework 4.7 or later. No administrator access is required.
Running apps are skipped when detectable. Websites, files and folders are opened normally.
Only one QuickLauncher instance can run per Windows session, across both editions.
'@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $portableReadme, (New-Object Text.UTF8Encoding($false)))
$portableZip = Join-Path $output "QuickLauncher-$version-windows-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $portableZip -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($portableZip)
try {
    $expectedEntries = @('LICENSE.txt', 'LICENSE-SmileySans.txt', 'QuickLauncher.exe', 'QuickLauncher.exe.config', 'README.txt', 'portable.flag')
    $actualEntries = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    if (@(Compare-Object ($expectedEntries | Sort-Object) $actualEntries).Count -gt 0) {
        throw 'Unexpected portable archive contents.'
    }
} finally { $archive.Dispose() }

$packages = @($portableZip)
if (-not $PortableOnly) {
    if (-not $InnoCompiler) {
        $candidates = @(
            (Join-Path $PSScriptRoot '.tools\inno\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $InnoCompiler) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $InnoCompiler = $command.Source }
        }
    }
    if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
        throw 'Inno Setup 6 compiler required. Pass -InnoCompiler or use -PortableOnly for ZIP only.'
    }
    & $InnoCompiler "/DAppVersion=$version" (Join-Path $PSScriptRoot 'packaging\windows.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $output "QuickLauncher-$version-windows-setup.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output missing.' }
    $packages += $installer
}
$packages | ForEach-Object { Write-Output "Packaged: $_" }
