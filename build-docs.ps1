$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$lockDir = Join-Path $scriptRoot 'site/.lunet/.build-lock'
$lunetLog = $null

function Clear-DocsOutputs {
    $wwwRoot = Join-Path $scriptRoot 'site/.lunet/build/www'
    Remove-Item $wwwRoot -Recurse -Force -ErrorAction SilentlyContinue
}

function Find-DocsMatches {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string[]]$Paths,
        [switch]$Fixed
    )

    if (Get-Command rg -ErrorAction SilentlyContinue) {
        $arguments = @('-n')
        if ($Fixed) {
            $arguments += '-F'
        }
        else {
            $arguments += '-e'
        }

        $arguments += $Pattern
        $arguments += $Paths
        return & rg @arguments
    }

    $matches = @()
    foreach ($path in $Paths) {
        $results = Select-String -Path $path -Pattern $Pattern -AllMatches -SimpleMatch:$Fixed -ErrorAction SilentlyContinue
        if ($results) {
            $matches += $results | ForEach-Object { "{0}:{1}:{2}" -f $_.Path, $_.LineNumber, $_.Line.Trim() }
        }
    }

    return $matches
}

while ($true) {
    if (-not (Test-Path $lockDir)) {
        try {
            New-Item -ItemType Directory -Path $lockDir -ErrorAction Stop | Out-Null
            break
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    Start-Sleep -Seconds 1
}

try {
    dotnet tool restore
    Clear-DocsOutputs

    Push-Location (Join-Path $scriptRoot 'site')
    try {
        $lunetLog = [System.IO.Path]::GetTempFileName()
        dotnet tool run lunet --stacktrace build 2>&1 | Tee-Object -FilePath $lunetLog
        $lunetErrors = Find-DocsMatches -Pattern 'ERR lunet' -Paths @($lunetLog)
        if ($lunetErrors) {
            throw "Lunet reported site build errors.`n$($lunetErrors -join "`n")"
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($lunetLog) {
        Remove-Item $lunetLog -Force -ErrorAction SilentlyContinue
    }

    Remove-Item $lockDir -Force -Recurse -ErrorAction SilentlyContinue
}
