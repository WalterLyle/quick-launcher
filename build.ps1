$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x is required.' }
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist') -Force | Out-Null
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.Linq.dll "/win32icon:$PSScriptRoot\assets\QuickLauncher-flat-1.ico" "/resource:$PSScriptRoot\assets\QuickLauncher-flat-1.ico,QuickLauncher.AppIcon" "/out:$PSScriptRoot\dist\QuickLauncher.exe" "$PSScriptRoot\src\AssemblyInfo.cs" "$PSScriptRoot\src\Program.cs" "$PSScriptRoot\tests\LauncherTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $PSScriptRoot\dist\QuickLauncher.exe"
