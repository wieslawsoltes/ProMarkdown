$ErrorActionPreference = 'Stop'

Push-Location (Join-Path $PSScriptRoot 'site')
try {
    dotnet tool restore
    dotnet tool run lunet --stacktrace serve
}
finally {
    Pop-Location
}
