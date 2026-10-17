$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x is required.' }
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
& $compiler /nologo /target:winexe "/win32manifest:$PSScriptRoot\src\app.manifest" /optimize+ /platform:anycpu /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll /reference:System.Web.Extensions.dll "/win32icon:$PSScriptRoot\assets\QuickLauncher-dynamic.ico" "/resource:$PSScriptRoot\assets\QuickLauncher-dynamic.ico,QuickLauncher.AppIcon" "/resource:$PSScriptRoot\assets\fonts\SmileySans-Oblique.ttf,QuickLauncher.SmileySans" "/out:$PSScriptRoot\dist\QuickLauncher.exe" "$PSScriptRoot\src\AssemblyInfo.cs" "$PSScriptRoot\src\Program.cs" "$PSScriptRoot\src\UpdateChecker.cs" "$PSScriptRoot\src\Theme.cs" "$PSScriptRoot\src\Localization.cs" "$PSScriptRoot\src\SmoothListView.cs" "$PSScriptRoot\src\SmoothFrame.cs" "$PSScriptRoot\tests\LauncherTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\app.config') -Destination (Join-Path $PSScriptRoot 'dist\QuickLauncher.exe.config') -Force
Write-Output "Built: $PSScriptRoot\dist\QuickLauncher.exe"
