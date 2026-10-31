# Builds dist\ScreenChick-Setup-<version>.exe, which carries its own .NET runtime.
# Needs Inno Setup 6: winget install --id JRSoftware.InnoSetup -e --scope user
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src\ScreenChick\ScreenChick.csproj'
$script = Join-Path $PSScriptRoot 'ScreenChick.iss'
$publishDir = Join-Path $PSScriptRoot 'publish'
$outputDir = Join-Path $root 'dist'

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it: winget install --id JRSoftware.InnoSetup -e --scope user' }

$version = (& dotnet msbuild $project -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) { throw 'Could not read the version from the project.' }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Native WPF libraries stay next to the exe: self-extracting them would slow every start
# and leave copies in the user's temp folder.
& dotnet publish $project -c Release -r win-x64 --self-contained true --nologo -v q `
    -p:PublishSingleFile=true -p:SatelliteResourceLanguages=ru -p:DebugType=none -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# The runtime is redistributed inside the installer, so its license notices must travel with it.
$runtime = (& dotnet msbuild $project -getProperty:BundledNETCoreAppPackageVersion).Trim()
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$licenses = New-Item (Join-Path $publishDir 'licenses') -ItemType Directory
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $licenses 'ScreenChick-LICENSE.txt')
Copy-Item "$packages\microsoft.netcore.app.runtime.win-x64\$runtime\LICENSE.TXT" (Join-Path $licenses 'dotnet-LICENSE.txt')
Copy-Item "$packages\microsoft.netcore.app.runtime.win-x64\$runtime\THIRD-PARTY-NOTICES.TXT" (Join-Path $licenses 'dotnet-THIRD-PARTY-NOTICES.txt')
Copy-Item "$packages\microsoft.windowsdesktop.app.runtime.win-x64\$runtime\LICENSE" (Join-Path $licenses 'windowsdesktop-LICENSE.txt')

& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" "/DOutputDir=$outputDir" $script
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

Remove-Item $publishDir -Recurse -Force
Get-Item (Join-Path $outputDir "ScreenChick-Setup-$version.exe")
