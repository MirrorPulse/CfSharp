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
