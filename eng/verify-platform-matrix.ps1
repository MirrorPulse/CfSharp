[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$expectedRuntimeIdentifiers = @('win-x64', 'win-arm64')
$packableProjects = @(
    'src/CfSharp.Native/CfSharp.Native.csproj',
    'src/CfSharp/CfSharp.csproj',
    'src/CfSharp.Storage.Sqlite/CfSharp.Storage.Sqlite.csproj',
    'samples/CfSharp.SampleProvider/CfSharp.SampleProvider.csproj'
)

foreach ($relativeProject in $packableProjects) {
    $projectPath = Join-Path $root $relativeProject
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Platform matrix project is missing: $relativeProject"
    }

    [xml] $project = Get-Content -LiteralPath $projectPath -Raw
    $runtimeNodes = @($project.SelectNodes("//*[local-name()='RuntimeIdentifiers']"))
    $actualRuntimeIdentifiers = @(
        $runtimeNodes |
            ForEach-Object { $_.InnerText -split ';' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
    )

    if (($actualRuntimeIdentifiers -join ';') -cne ($expectedRuntimeIdentifiers -join ';')) {
        throw "Unexpected RuntimeIdentifiers in $relativeProject. Expected '$($expectedRuntimeIdentifiers -join ';')', got '$($actualRuntimeIdentifiers -join ';')'."
    }

    if ($actualRuntimeIdentifiers -contains 'win-x86') {
        throw "x86 is outside the stable support matrix but is declared by $relativeProject."
    }
}

$packageProjectPath = Join-Path $root 'samples/CfSharp.SampleProvider.Package/CfSharp.SampleProvider.Package.wapproj'
if (-not (Test-Path -LiteralPath $packageProjectPath -PathType Leaf)) {
    throw 'MSIX package project is missing: samples/CfSharp.SampleProvider.Package/CfSharp.SampleProvider.Package.wapproj'
}

[xml] $packageProject = Get-Content -LiteralPath $packageProjectPath -Raw
$platformNode = $packageProject.SelectSingleNode("//*[local-name()='Platforms']")
if ($null -eq $platformNode) {
    throw 'MSIX package project does not declare a Platforms property.'
}
$actualPackagePlatforms = @(
    $platformNode.InnerText -split ';' |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() }
)
$expectedPackagePlatforms = @('x64', 'ARM64')
if (($actualPackagePlatforms -join ';') -cne ($expectedPackagePlatforms -join ';')) {
    throw "Unexpected MSIX package platforms. Expected '$($expectedPackagePlatforms -join ';')', got '$($actualPackagePlatforms -join ';')'."
}

if ($actualPackagePlatforms -contains 'x86') {
    throw 'x86 is outside the stable support matrix but is declared by the MSIX package.'
}

Write-Output "Supported runtime matrix: $($expectedRuntimeIdentifiers -join ', ')"
Write-Output "MSIX package platforms: $($expectedPackagePlatforms -join ', ')"
Write-Output 'x86 policy: explicitly excluded from the stable release matrix.'
