param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$AssemblyInfo = (Join-Path $PSScriptRoot 'src\AssemblyInfo.cs')
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x is required.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $compiler /nologo /target:winexe "/win32manifest:$PSScriptRoot\src\app.manifest" /optimize+ /platform:anycpu /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$PSScriptRoot\assets\QuickLauncher-dynamic.ico" "/resource:$PSScriptRoot\assets\QuickLauncher-dynamic.ico,QuickLauncher.AppIcon" "/resource:$PSScriptRoot\assets\fonts\SmileySans-Oblique.ttf,QuickLauncher.SmileySans" "/out:$OutputDirectory\QuickLauncher.exe" "$AssemblyInfo" "$PSScriptRoot\src\Program.cs" "$PSScriptRoot\src\UpdateChecker.cs" "$PSScriptRoot\src\AutoUpdater.cs" "$PSScriptRoot\src\Theme.cs" "$PSScriptRoot\src\Localization.cs" "$PSScriptRoot\src\SmoothListView.cs" "$PSScriptRoot\src\SmoothFrame.cs" "$PSScriptRoot\tests\LauncherTests.cs" "$PSScriptRoot\tests\UpdaterTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\app.config') -Destination (Join-Path $OutputDirectory 'QuickLauncher.exe.config') -Force
Write-Output "Built: $OutputDirectory\QuickLauncher.exe"
