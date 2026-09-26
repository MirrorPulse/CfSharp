[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $Update,
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$baselinePath = Join-Path $root 'eng/api-baseline.json'
$artifactDirectory = Join-Path $root 'artifacts/api-baseline'
$currentPath = Join-Path $artifactDirectory 'current.json'
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

$testProject = Join-Path $root 'tests/CfSharp.Tests/CfSharp.Tests.csproj'
$env:CFSHARP_API_BASELINE_OUTPUT = $currentPath
$env:CFSHARP_API_BASELINE_CONFIGURATION = $Configuration
& dotnet test $testProject --configuration $Configuration --no-build --no-restore `
    -p:TargetPlatformDisplayName=Windows `
    --filter 'FullyQualifiedName~ApiBaselineInventoryTests'
if ($LASTEXITCODE -ne 0) {
    throw "API baseline inventory test failed with exit code $LASTEXITCODE."
}
Remove-Item Env:CFSHARP_API_BASELINE_OUTPUT -ErrorAction SilentlyContinue
Remove-Item Env:CFSHARP_API_BASELINE_CONFIGURATION -ErrorAction SilentlyContinue

if ($Update) {
    Copy-Item -LiteralPath $currentPath -Destination $baselinePath -Force
    Write-Output "Updated API baseline: $baselinePath"
    exit 0
}

if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw "API baseline is missing: $baselinePath. Run with -Update once for an intentional baseline change."
}

$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$current = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($property in $baseline.assemblies.psobject.Properties) {
    $currentAssembly = $current.assemblies.psobject.Properties[$property.Name]
    if ($null -eq $currentAssembly) {
        $missing.Add("assembly $($property.Name)")
        continue
    }

    $currentMembers = @{}
    foreach ($member in @($currentAssembly.Value)) {
        $currentMembers[[string]$member] = $true
    }
    foreach ($member in @($property.Value)) {
        if (-not $currentMembers.ContainsKey([string]$member)) {
            $missing.Add("$($property.Name): $member")
        }
    }
}

if ($missing.Count -ne 0) {
    $missing | Set-Content -LiteralPath (Join-Path $artifactDirectory 'breaking-diff.txt')
    throw "Managed API baseline regression detected ($($missing.Count) missing entries). Review artifacts/api-baseline/breaking-diff.txt or use -Update only for an approved change."
}

Write-Output "Managed API baseline passed; additive entries are allowed for preview builds."
