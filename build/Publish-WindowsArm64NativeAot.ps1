[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) { throw 'Windows Native AOT publishing requires Windows.' }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$toolchain = & $vswhere -latest -prerelease -products '*' -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $toolchain) {
    throw 'Install Visual Studio 2026 C++ desktop build tools and ARM64 tools before publishing.'
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
# Keep the output path for callers of this script.
$outputPath = Join-Path $repositoryRoot 'artifacts/aot-investigation/patched'
Push-Location $repositoryRoot
try {
    & dotnet publish src/ProMarkdown.Sample/ProMarkdown.Sample.csproj -c Release -f net11.0 -r win-arm64 -p:PublishAot=true -p:TrimmerSingleWarn=false -p:UseSharedCompilation=false -m:1 -o $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'Windows ARM64 Native AOT publishing failed.' }
}
finally {
    Pop-Location
}
