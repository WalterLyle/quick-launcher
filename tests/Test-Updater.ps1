# Runs only on a disposable runner: exercises real process exit, replacement, restart and setup.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) { throw 'Updater integration tests require a disposable GitHub runner.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $projectRoot 'VERSION') -Raw).Trim()
$scratch = Join-Path $env:RUNNER_TEMP ('QuickLauncher-updater-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$baseline = Join-Path $scratch 'baseline'
$baselineInfo = Join-Path $scratch 'AssemblyInfo.cs'
$assemblyText = [IO.File]::ReadAllText((Join-Path $projectRoot 'src\AssemblyInfo.cs'))
[IO.File]::WriteAllText($baselineInfo, $assemblyText.Replace($version + '.0', '0.8.0.0'))
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $baseline -AssemblyInfo $baselineInfo
$packageRoot = Join-Path $projectRoot 'dist\release'
$newBinary = Join-Path $projectRoot 'dist\QuickLauncher.exe'
$expectedHash = (Get-FileHash $newBinary -Algorithm SHA256).Hash

function Invoke-Upgrade([bool]$Portable) {
    $edition = if ($Portable) { 'portable' } else { 'installed' }
    $target = Join-Path $scratch ($edition + ' 中文 space')
    New-Item -ItemType Directory -Path $target | Out-Null
    $setup = Join-Path $packageRoot "QuickLauncher-$version-windows-setup.exe"
    if (-not $Portable) {
        $installed = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="' + $target + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($installed.ExitCode -ne 0) { throw 'Initial installation failed' }
    }
    Copy-Item (Join-Path $baseline 'QuickLauncher.exe') (Join-Path $target 'QuickLauncher.exe') -Force
    Copy-Item (Join-Path $baseline 'QuickLauncher.exe.config') (Join-Path $target 'QuickLauncher.exe.config') -Force
    $configDir = if ($Portable) { Join-Path $target 'data' } else { Join-Path $env:LOCALAPPDATA 'QuickLauncher' }
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    if ($Portable) { [IO.File]::WriteAllText((Join-Path $target 'portable.flag'), '') }
    $config = Join-Path $configDir 'items.xml'
    # A nonexistent target would cause a dialog if --updated accidentally ran automatic startup.
    [IO.File]::WriteAllText($config, '<launcher autoOpen="true" language="en"><item path="Z:\missing-update-test.exe" /></launcher>')
    $originalConfig = (Get-FileHash $config -Algorithm SHA256).Hash
    $work = Join-Path ([IO.Path]::GetTempPath()) ('QuickLauncher-update-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $helper = Join-Path $work 'UpdateHelper.exe'
    Copy-Item $newBinary $helper
    Copy-Item ($newBinary + '.config') ($helper + '.config')
    $packageName = if ($Portable) { "QuickLauncher-$version-windows-portable.zip" } else { "QuickLauncher-$version-windows-setup.exe" }
    $package = Join-Path $work $packageName
    Copy-Item (Join-Path $packageRoot $packageName) $package
    $oldProcess = Start-Process (Join-Path $target 'QuickLauncher.exe') -ArgumentList '--updated' -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    $oldProcess.Refresh()
    if ($oldProcess.HasExited) { throw 'Baseline launcher did not remain running' }
    $plan = New-Object Xml.XmlDocument
    $root = $plan.CreateElement('update'); $plan.AppendChild($root) | Out-Null
    $values = @{
        target = $target; package = $package; hash = (Get-FileHash $package -Algorithm SHA256).Hash
        version = $version; portable = $Portable.ToString().ToLowerInvariant(); restart = 'true'; english = 'true'
        parent = $oldProcess.Id; started = $oldProcess.StartTime.ToUniversalTime().Ticks
    }
    foreach ($key in $values.Keys) { $root.SetAttribute($key, [string]$values[$key]) }
    $planPath = Join-Path $work 'update.xml'; $plan.Save($planPath)
    $helperProcess = Start-Process $helper -ArgumentList ('--apply-update "' + $planPath + '"') -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 1
    if ($helperProcess.HasExited) { throw 'Updater did not wait for old process' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $target 'QuickLauncher.exe')).FileVersion -ne '0.8.0.0') { throw 'Replaced a running launcher' }
    if (-not $oldProcess.CloseMainWindow() -or -not $oldProcess.WaitForExit(10000)) { throw 'Could not close baseline launcher' }
    if (-not $helperProcess.WaitForExit(60000) -or $helperProcess.ExitCode -ne 0) {
        if (Test-Path (Join-Path $work 'result.txt')) { Get-Content (Join-Path $work 'result.txt') }
        throw 'Updater helper failed'
    }
    if ((Get-FileHash (Join-Path $target 'QuickLauncher.exe') -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Updated binary mismatch' }
    if ((Get-FileHash $config -Algorithm SHA256).Hash -ne $originalConfig) { throw 'Update changed user configuration' }
    $restarted = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $restarted = Get-Process QuickLauncher -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target 'QuickLauncher.exe') }
        if ($restarted -and $restarted.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $restarted -or $restarted.MainWindowTitle -ne "Quick Launcher v$version") { throw 'New version did not restart with saved language and version' }
    if (-not $restarted.CloseMainWindow() -or -not $restarted.WaitForExit(10000)) { throw 'Restarted app did not close normally (possible startup error dialog)' }
    if (-not $Portable) {
        $uninstall = Start-Process (Join-Path $target 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($uninstall.ExitCode -ne 0 -or (Test-Path (Join-Path $target 'QuickLauncher.exe'))) { throw 'Upgraded app uninstall failed' }
        if ((Get-FileHash $config -Algorithm SHA256).Hash -ne $originalConfig) { throw 'Uninstall removed personal settings' }
    }
    Write-Output "PASS: $edition update from 0.8.0 to $version, wait for exit, restart, user settings and language preservation."
}
Invoke-Upgrade $true
Invoke-Upgrade $false
