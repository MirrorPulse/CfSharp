[CmdletBinding()]
param(
    [string]$Root = 'artifacts\docs\cfsharp'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$resolvedRoot = (Resolve-Path (Join-Path $repoRoot $Root)).Path
$manifestPath = Join-Path $resolvedRoot 'manifest.json'

if (-not (Test-Path (Join-Path $resolvedRoot 'index.html'))) {
    throw "Documentation index is missing: $resolvedRoot\index.html"
}
if (-not (Test-Path $manifestPath)) {
    throw "Documentation manifest is missing: $manifestPath"
}

$requiredFiles = @(
    'toc.html',
    'public\main.css',
    'public\main.js',
    'api\CfSharp.html',
    'articles\getting-started.html'
)
foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $resolvedRoot $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Documentation bundle is missing required file: $requiredFile"
    }
}

$tocHtml = Get-Content -LiteralPath (Join-Path $resolvedRoot 'toc.html') -Raw
foreach ($navigationMarker in @('title="Overview"', '>Guides</a>', '>API reference</a>')) {
    if ($tocHtml -notlike "*$navigationMarker*") {
        throw "Documentation navigation is missing expected marker: $navigationMarker"
    }
}

$indexHtml = Get-Content -LiteralPath (Join-Path $resolvedRoot 'index.html') -Raw
$gettingStartedHtml = Get-Content -LiteralPath (Join-Path $resolvedRoot 'articles\getting-started.html') -Raw
if ($indexHtml -notlike '*blob/main/README.md#L1*') {
    throw 'The documentation home does not link Edit this page to README.md.'
}
if ($gettingStartedHtml -notlike '*blob/main/docs/getting-started.md#L1*') {
    throw 'The getting-started guide does not link Edit this page to docs/getting-started.md.'
}
if ($indexHtml -notlike '*public/main.js*') {
    throw 'The documentation home does not load the custom navigation script.'
}

$contributionPages = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Filter '*.html' |
    ForEach-Object { [pscustomobject]@{ Path = $_.FullName; Text = Get-Content -LiteralPath $_.FullName -Raw } } |
    Where-Object { $_.Text -like '*class="edit-link"*' }
if ($contributionPages | Where-Object { $_.Text -like '*artifacts/docs/workspace*' }) {
    throw 'A generated Edit this page link still exposes the staging artifacts path.'
}

$themeCss = Get-Content -LiteralPath (Join-Path $resolvedRoot 'public\main.css') -Raw
if ($themeCss -notlike '*--cf-radius-xl*') {
    throw 'Documentation custom theme marker --cf-radius-xl is missing.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($property in @('schemaVersion', 'project', 'sourceCommit', 'generatedAtUtc', 'generator', 'configuration', 'contentRoot', 'files')) {
    if ($null -eq $manifest.$property) {
        throw "Documentation manifest is missing '$property'."
    }
}
if ($manifest.schemaVersion -ne 1 -or $manifest.project -ne 'CfSharp' -or $manifest.contentRoot -ne 'cfsharp') {
    throw 'Documentation manifest identity is invalid.'
}
if ($manifest.generator.name -ne 'DocFX') {
    throw 'Documentation manifest generator is not DocFX.'
}

$forbiddenExtensions = @('.cs', '.csproj', '.dll', '.pdb', '.nupkg', '.db', '.sqlite', '.user')
$forbidden = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File | Where-Object {
    $forbiddenExtensions -contains $_.Extension.ToLowerInvariant()
}
if ($forbidden) {
    throw "Generated documentation contains forbidden implementation artifacts: $($forbidden.FullName -join ', ')"
}

$actualFiles = @(
    Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File |
        Where-Object { $_.FullName -ne $manifestPath } |
        ForEach-Object {
            [System.IO.Path]::GetRelativePath($resolvedRoot, $_.FullName).Replace('\','/')
        }
)
$manifestFiles = @($manifest.files | ForEach-Object { $_.path })
$inventoryDifference = Compare-Object -ReferenceObject @($actualFiles | Sort-Object) -DifferenceObject @($manifestFiles | Sort-Object)
if ($null -ne $inventoryDifference) {
    throw 'Documentation manifest file inventory does not match the generated output.'
}

foreach ($record in $manifest.files) {
    $path = Join-Path $resolvedRoot ($record.path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Manifest file is missing: $($record.path)"
    }
    $actual = Get-Item -LiteralPath $path
    if ($actual.Length -ne [int64]$record.bytes) {
        throw "Manifest byte count is wrong: $($record.path)"
    }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -cne $record.sha256) {
        throw "Manifest hash is wrong: $($record.path)"
    }
}

Write-Output "Documentation bundle verified: $($manifest.files.Count) files, source $($manifest.sourceCommit)."
