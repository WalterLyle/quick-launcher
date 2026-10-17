# This changes the CI runner's user installation state. Never run it on a user's desktop.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) {
    throw 'Installer smoke-test is restricted to a disposable GitHub Actions runner.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'VERSION') -Raw).Trim()
$installer = Join-Path $projectRoot "dist\release\QuickLauncher-$version-windows-setup.exe"
$runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
$scratch = [IO.Path]::GetFullPath((Join-Path $runnerRoot ('QuickLauncher-smoke-' + [Guid]::NewGuid().ToString('N'))))
if (-not $scratch.StartsWith($runnerRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test directory.' }
$installDir = Join-Path $scratch 'installed'
$userDataDir = Join-Path $env:LOCALAPPDATA 'QuickLauncher'
$userConfig = Join-Path $userDataDir 'items.xml'
if (Test-Path -LiteralPath $userDataDir) { throw 'Refusing to touch an existing user data directory.' }
New-Item -ItemType Directory -Path $userDataDir -Force | Out-Null
[IO.File]::WriteAllText($userConfig, '<launcher><item path="https://example.com/" /></launcher>')
$originalHash = (Get-FileHash -LiteralPath $userConfig -Algorithm SHA256).Hash

$installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""', ('/DIR="' + $installDir + '"'))
$installProcess = Start-Process -FilePath $installer -ArgumentList $installArguments -Wait -PassThru -WindowStyle Hidden
if ($installProcess.ExitCode -ne 0) { throw 'Silent installation failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $installDir 'QuickLauncher.exe'))) { throw 'Installed executable missing.' }
if (Test-Path -LiteralPath (Join-Path $installDir 'portable.flag')) { throw 'Installed edition is incorrectly portable.' }
if ((Get-FileHash -LiteralPath $userConfig -Algorithm SHA256).Hash -ne $originalHash) { throw 'Installation changed the user list.' }

$uninstaller = Join-Path $installDir 'unins000.exe'
if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'Uninstall entry missing.' }
$uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru -WindowStyle Hidden
if ($uninstallProcess.ExitCode -ne 0) { throw 'Silent uninstall failed.' }
if (Test-Path -LiteralPath (Join-Path $installDir 'QuickLauncher.exe')) { throw 'Uninstall did not remove the application.' }
if (-not (Test-Path -LiteralPath $userConfig)) { throw 'Uninstall deleted the user list.' }
if ((Get-FileHash -LiteralPath $userConfig -Algorithm SHA256).Hash -ne $originalHash) { throw 'Uninstall changed the user list.' }
Write-Output 'PASS: installation, uninstall entry, uninstall and user-list preservation.'
