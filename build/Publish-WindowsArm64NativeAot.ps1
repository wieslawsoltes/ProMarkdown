[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Set-ExpectedReplacement {
    param([string]$Path, [string]$Original, [string]$Replacement, [int]$Count = 1)

    $content = [IO.File]::ReadAllText($Path)
    $matches = ([regex]::Matches($content, [regex]::Escape($Original))).Count
    if ($matches -eq $Count) {
        [IO.File]::WriteAllText($Path, $content.Replace($Original, $Replacement))
    }
    elseif ($matches -ne 0 -or ([regex]::Matches($content, [regex]::Escape($Replacement))).Count -ne $Count) {
        throw "Unexpected Avalonia source content in $Path. Recreate the isolated source directory."
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$sourceCommit = '5825bf8c66fba0c11e67c980d8ece6444576bffa'
$archiveHash = '88D5B53AAE8D93290CE6EE02B66BEAB5AC9BB6DEC250636DE5B897378DF20D4E'
$auditRoot = Join-Path $repositoryRoot 'artifacts/aot-investigation'
$upstreamRoot = Join-Path $auditRoot 'upstream'
$archivePath = Join-Path $upstreamRoot 'avalonia-12.1.5.zip'
$sourceRoot = Join-Path $upstreamRoot "Avalonia-$sourceCommit"

if (-not $IsWindows) { throw 'Windows Native AOT publishing requires Windows.' }
[xml]$packageVersions = Get-Content (Join-Path $repositoryRoot 'Directory.Packages.props') -Raw
$avaloniaVersion = ($packageVersions.Project.ItemGroup.PackageVersion | Where-Object Include -EQ 'Avalonia').Version
if ($avaloniaVersion -ne '12.1.5') { throw 'Reaudit this source patch before using a different Avalonia version.' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$toolchain = & $vswhere -latest -prerelease -products '*' -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $toolchain) {
    throw 'Install Visual Studio 2026 C++ desktop build tools and ARM64 tools before publishing.'
}

New-Item -ItemType Directory -Force $upstreamRoot | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest "https://codeload.github.com/AvaloniaUI/Avalonia/zip/$sourceCommit" -OutFile $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $archiveHash) {
    throw 'The Avalonia source archive hash does not match the audited source.'
}
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $upstreamRoot
}

$backendRoot = Join-Path $sourceRoot 'src/Windows/Avalonia.Win32'
# Match the native BOOL ABI in both interfaces; MicroCom regenerates the callback signatures.
Set-ExpectedReplacement (Join-Path $backendRoot 'DComposition/dcomp.idl') 'IntPtr hwnd, bool topmost,' 'IntPtr hwnd, BOOL topmost,' 2
Set-ExpectedReplacement (Join-Path $backendRoot 'DComposition/DirectCompositedWindow.cs') 'CreateTargetForHwnd(WindowInfo.Handle, false)' 'CreateTargetForHwnd(WindowInfo.Handle, 0)'

# Rebuild just the backend against the exact released implementation assemblies.
# Public reference assemblies intentionally exclude the internals needed by platform backends.
$backendProject = Join-Path $backendRoot 'Avalonia.Win32.csproj'
Set-ExpectedReplacement $backendProject '<ProjectReference Include="..\..\..\packages\Avalonia\Avalonia.csproj" />' '<PackageReference Include="Avalonia" VersionOverride="12.1.5" ExcludeAssets="compile" /><PackageReference Include="MicroCom.Runtime" /><Reference Include="$(NuGetPackageRoot)avalonia/12.1.5/lib/net10.0/*.dll" />'
Set-ExpectedReplacement $backendProject '<ProjectReference Include="..\Avalonia.Win32.Automation\Avalonia.Win32.Automation.csproj" />' '<Reference Include="Avalonia.Win32.Automation"><HintPath>$(NuGetPackageRoot)avalonia.win32/12.1.5/lib/net10.0/Avalonia.Win32.Automation.dll</HintPath></Reference>'

Push-Location $repositoryRoot
try {
    & dotnet build $backendProject -c Release -f net10.0 -p:AvsSkipBuildingLegacyTargetFrameworks=True -p:UseSharedCompilation=false -m:1
    if ($LASTEXITCODE -ne 0) { throw 'The patched Avalonia Win32 backend build failed.' }

    $patchedAssembly = Join-Path $backendRoot 'bin/Release/net10.0/Avalonia.Win32.dll'
    & dotnet publish src/ProMarkdown.Sample/ProMarkdown.Sample.csproj -c Release -f net11.0 -r win-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -p:UseSharedCompilation=false "-p:AvaloniaWin32AotAssembly=$patchedAssembly" -m:1 -o (Join-Path $auditRoot 'patched')
    if ($LASTEXITCODE -ne 0) { throw 'Windows ARM64 Native AOT publishing failed.' }
}
finally {
    Pop-Location
}
