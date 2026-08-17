$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-docs.ps1')

$docRoot = Join-Path $PSScriptRoot 'site/.lunet/build/www'

function Find-GeneratedMatches {
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

$requiredFiles = @(
    (Join-Path $docRoot 'index.html'),
    (Join-Path $docRoot 'articles/index.html'),
    (Join-Path $docRoot 'articles/getting-started/index.html'),
    (Join-Path $docRoot 'articles/getting-started/overview/index.html'),
    (Join-Path $docRoot 'articles/getting-started/installation/index.html'),
    (Join-Path $docRoot 'articles/getting-started/first-document/index.html'),
    (Join-Path $docRoot 'articles/getting-started/running-the-sample/index.html'),
    (Join-Path $docRoot 'articles/markdown/index.html'),
    (Join-Path $docRoot 'articles/markdown/markdown-text-block/index.html'),
    (Join-Path $docRoot 'articles/markdown/rendering-services/index.html'),
    (Join-Path $docRoot 'articles/markdown/editing/index.html'),
    (Join-Path $docRoot 'articles/markdown/source-mapping/index.html'),
    (Join-Path $docRoot 'articles/markdown/theming/index.html'),
    (Join-Path $docRoot 'articles/markdown/task-lists/index.html'),
    (Join-Path $docRoot 'articles/markdown/plugin-ecosystem/index.html'),
    (Join-Path $docRoot 'articles/extensions/index.html'),
    (Join-Path $docRoot 'articles/extensions/plugin-model/index.html'),
    (Join-Path $docRoot 'articles/extensions/parser-plugins/index.html'),
    (Join-Path $docRoot 'articles/extensions/rendering-plugins/index.html'),
    (Join-Path $docRoot 'articles/extensions/editor-plugins/index.html'),
    (Join-Path $docRoot 'articles/extensions/block-templates/index.html'),
    (Join-Path $docRoot 'articles/development/index.html'),
    (Join-Path $docRoot 'articles/development/build-test-and-docs/index.html'),
    (Join-Path $docRoot 'articles/reference/index.html'),
    (Join-Path $docRoot 'articles/reference/controls-api/index.html'),
    (Join-Path $docRoot 'articles/reference/services-api/index.html'),
    (Join-Path $docRoot 'articles/reference/rendering-models-api/index.html'),
    (Join-Path $docRoot 'articles/reference/repository-structure/index.html')
)

$bundleCss = Join-Path $docRoot 'css/lite.css'

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Required docs output missing: $file"
    }
}

if (-not (Test-Path $bundleCss)) {
    throw "Required docs output missing: $bundleCss"
}

$htmlFiles = Get-ChildItem -Path $docRoot -Filter *.html -Recurse -File | ForEach-Object { $_.FullName }
$rawMarkdownLinks = Find-GeneratedMatches -Pattern 'href="[^"]*\.md([?#"][^"]*)?"' -Paths $htmlFiles
if ($rawMarkdownLinks) {
    $internalMarkdownLinks = $rawMarkdownLinks | Where-Object { $_ -notmatch 'href="https?://' }
    if ($internalMarkdownLinks) {
        throw "Generated docs contain raw .md links.`n$($internalMarkdownLinks -join "`n")"
    }
}

$readmeRoutes = Find-GeneratedMatches -Pattern 'href="[^"]*/readme([?#"][^"]*)?"' -Paths $htmlFiles
if ($readmeRoutes) {
    throw "Generated docs contain /readme routes instead of directory routes.`n$($readmeRoutes -join "`n")"
}

$rawMarkdownOutputs = Get-ChildItem -Path (Join-Path $docRoot 'articles') -Filter *.md -Recurse -ErrorAction SilentlyContinue
if ($rawMarkdownOutputs.Count -gt 0) {
    throw "Generated docs still contain raw .md article outputs.`n$($rawMarkdownOutputs.FullName -join "`n")"
}

$mitFooter = Find-GeneratedMatches -Pattern 'MIT license' -Paths @((Join-Path $docRoot 'index.html')) -Fixed
if (-not $mitFooter) {
    throw 'Generated site footer is missing the project MIT license text.'
}

$repoLink = Find-GeneratedMatches -Pattern 'https://github.com/wieslawsoltes/ProMarkdown' -Paths @((Join-Path $docRoot 'index.html')) -Fixed
if (-not $repoLink) {
    throw 'Generated home page is missing the repository link.'
}

$basepathLinks = Find-GeneratedMatches -Pattern '/ProMarkdown/articles/' -Paths @((Join-Path $docRoot 'index.html')) -Fixed
if (-not $basepathLinks) {
    throw 'Generated home page is missing basepath-prefixed article links.'
}

$heroLead = Find-GeneratedMatches -Pattern '<p class="lead"><strong>ProMarkdown</strong>' -Paths @((Join-Path $docRoot 'index.html')) -Fixed
if (-not $heroLead) {
    throw 'Generated home page is missing the rendered hero lead paragraph.'
}

$heroSelector = Find-GeneratedMatches -Pattern '.pm-hero' -Paths @($bundleCss) -Fixed
if (-not $heroSelector) {
    throw 'Generated docs bundle is missing the .pm-hero selector.'
}

$linkCardSelector = Find-GeneratedMatches -Pattern '.pm-link-card' -Paths @($bundleCss) -Fixed
if (-not $linkCardSelector) {
    throw 'Generated docs bundle is missing the .pm-link-card selector.'
}
