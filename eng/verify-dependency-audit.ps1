[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "artifacts/dependency-audit"
$solution = Join-Path $repoRoot "CfSharp.sln"
$requiredFiles = @(
    "Directory.Build.props",
    "Directory.Packages.props",
    "NuGet.config",
    "global.json",
    "LICENSE",
    "NOTICE"
)

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

foreach ($requiredFile in $requiredFiles)
{
    $path = Join-Path $repoRoot $requiredFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf))
    {
        throw "Required dependency-policy file is missing: $requiredFile"
    }
}

$projectRoots = @("src", "tests", "samples") | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $repoRoot $_) -Filter *.csproj -File -Recurse
} | Sort-Object FullName

if ($projectRoots.Count -eq 0)
{
    throw "No SDK-style projects were found for dependency auditing."
}

function Invoke-DotnetJson
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $lines = @(& dotnet @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($Arguments -join ' ') failed:`n$($lines -join "`n")"
    }

    $text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    try
    {
        return $text | ConvertFrom-Json -Depth 100
    }
    catch
    {
        throw "dotnet $($Arguments -join ' ') did not return valid JSON: $($_.Exception.Message)"
    }
}

$previousCi = $env:CI
$env:CI = "true"
try
{
    $restoreLines = @(& dotnet restore $solution --locked-mode --nologo 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Locked restore failed:`n$($restoreLines -join "`n")"
    }
}
finally
{
    if ($null -eq $previousCi)
    {
        Remove-Item Env:CI -ErrorAction SilentlyContinue
    }
    else
    {
        $env:CI = $previousCi
    }
}

$inventoryReports = [System.Collections.Generic.List[object]]::new()
$vulnerabilityReports = [System.Collections.Generic.List[object]]::new()
$packages = @{}

foreach ($project in $projectRoots)
{
    $relativeProject = $project.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    $inventory = Invoke-DotnetJson -Arguments @(
        "list",
        $project.FullName,
        "package",
        "--include-transitive",
        "--format", "json",
        "--no-restore")
    $vulnerabilities = Invoke-DotnetJson -Arguments @(
        "list",
        $project.FullName,
        "package",
        "--vulnerable",
        "--include-transitive",
        "--format", "json",
        "--no-restore")

    $inventoryReports.Add([ordered]@{
        project = $relativeProject
        report = $inventory
    })
    $vulnerabilityReports.Add([ordered]@{
        project = $relativeProject
        report = $vulnerabilities
    })

    $vulnerabilityJson = $vulnerabilities | ConvertTo-Json -Depth 100 -Compress
    if ($vulnerabilityJson -match '"vulnerabilities"\s*:')
    {
        throw "NuGet vulnerability advisories were reported for $relativeProject. See the audit artifact."
    }

    foreach ($framework in @($inventory.projects.frameworks))
    {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages))
        {
            if ($null -eq $package -or [string]::IsNullOrWhiteSpace([string]$package.id))
            {
                continue
            }

            $version = [string]$package.resolvedVersion
            if ([string]::IsNullOrWhiteSpace($version))
            {
                continue
            }

            $key = "$($package.id)|$version"
            if (-not $packages.ContainsKey($key))
            {
                $packages[$key] = [ordered]@{
                    id = [string]$package.id
                    version = $version
                    projects = [System.Collections.Generic.List[string]]::new()
                }
            }
            if (-not $packages[$key].projects.Contains($relativeProject))
            {
                $packages[$key].projects.Add($relativeProject)
            }
        }
    }
}

$globalPackagesRoot = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES))
{
    $env:NUGET_PACKAGES
}
else
{
    Join-Path $env:USERPROFILE ".nuget\packages"
}

$spdxPackages = [System.Collections.Generic.List[object]]::new()
foreach ($package in @($packages.Values | Sort-Object id, version))
{
    $safeName = (($package.id + "-" + $package.version) -replace '[^A-Za-z0-9.-]', '-')
    $spdxId = "SPDXRef-Package-$safeName"
    $packageRoot = Join-Path (Join-Path $globalPackagesRoot $package.id.ToLowerInvariant()) $package.version.ToLowerInvariant()
    $nuspec = Get-ChildItem -LiteralPath $packageRoot -Filter *.nuspec -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $licenseDeclared = "NOASSERTION"
    $licenseSource = $null
    if ($null -ne $nuspec)
    {
        [xml]$nuspecDocument = Get-Content -LiteralPath $nuspec.FullName -Raw
        $metadata = $nuspecDocument.package.metadata
        if ($null -ne $metadata.license -and $metadata.license.type -eq "expression")
        {
            $licenseDeclared = $metadata.license.InnerText.Trim()
        }
        elseif ($null -ne $metadata.licenseUrl -and -not [string]::IsNullOrWhiteSpace([string]$metadata.licenseUrl))
        {
            $licenseSource = [string]$metadata.licenseUrl
        }
    }

    $nupkg = Get-ChildItem -LiteralPath $packageRoot -Filter *.nupkg -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $checksums = @()
    if ($null -ne $nupkg)
    {
        $checksums = @([ordered]@{
            algorithm = "SHA256"
            checksumValue = (Get-FileHash -LiteralPath $nupkg.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }

    $spdxPackage = [ordered]@{
        SPDXID = $spdxId
        name = $package.id
        versionInfo = $package.version
        downloadLocation = "https://api.nuget.org/v3-flatcontainer/$($package.id.ToLowerInvariant())/$($package.version.ToLowerInvariant())/$($package.id.ToLowerInvariant()).$($package.version).nupkg"
        licenseConcluded = $licenseDeclared
        licenseDeclared = $licenseDeclared
        copyrightText = "NOASSERTION"
        checksums = $checksums
        externalRefs = @([ordered]@{
            referenceCategory = "PACKAGE-MANAGER"
            referenceType = "purl"
            referenceLocator = "pkg:nuget/$($package.id)@$($package.version)"
        })
    }
    if ($null -ne $licenseSource)
    {
        $spdxPackage.licenseComments = "NuGet licenseUrl: $licenseSource"
    }
    $spdxPackages.Add($spdxPackage)
}

$commitOutput = ((& git -C $repoRoot rev-parse HEAD 2>$null) | Select-Object -First 1)
$branchOutput = ((& git -C $repoRoot branch --show-current 2>$null) | Select-Object -First 1)
$sdkOutput = ((& dotnet --version 2>$null) | Select-Object -First 1)
$commit = if ($null -eq $commitOutput) { "unknown" } else { $commitOutput.ToString().Trim() }
$branch = if ([string]::IsNullOrWhiteSpace([string]$branchOutput)) { "(detached)" } else { $branchOutput.ToString().Trim() }
$sdkVersion = if ($null -eq $sdkOutput) { "unknown" } else { $sdkOutput.ToString().Trim() }
$generatedAt = [DateTimeOffset]::UtcNow.ToString("O")
$rootSpdxId = "SPDXRef-CfSharpSource"
$relationships = [System.Collections.Generic.List[object]]::new()
foreach ($package in $spdxPackages)
{
    $relationships.Add([ordered]@{
        spdxElementId = $rootSpdxId
        relationshipType = "DEPENDS_ON"
        relatedSpdxElement = $package.SPDXID
    })
}

$spdx = [ordered]@{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "CfSharp dependency inventory"
    documentNamespace = "https://github.com/MirrorPulse/CfSharp/sbom/$commit"
    creationInfo = [ordered]@{
        created = $generatedAt
        creators = @("Tool: CfSharp verify-dependency-audit.ps1", "Organization: MirrorPulse Team")
    }
    packages = @([ordered]@{
        SPDXID = $rootSpdxId
        name = "CfSharp source"
        versionInfo = $commit
        downloadLocation = "https://github.com/MirrorPulse/CfSharp/tree/$commit"
        licenseConcluded = "Apache-2.0"
        licenseDeclared = "Apache-2.0"
        copyrightText = "Copyright (c) 2026 MirrorPulse Team"
    }) + @($spdxPackages)
    relationships = @($relationships)
}

$lockFiles = $projectRoots | ForEach-Object {
    Get-ChildItem -LiteralPath $_.Directory.FullName -Filter packages.lock.json -File
} | Sort-Object FullName
$evidenceFiles = @($requiredFiles + "eng/verify-dependency-audit.ps1") + @(
    $lockFiles | ForEach-Object {
        $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    }
)
$fileEvidence = foreach ($relativeFile in $evidenceFiles)
{
    $path = Join-Path $repoRoot $relativeFile
    if (Test-Path -LiteralPath $path -PathType Leaf)
    {
        [ordered]@{
            path = $relativeFile
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

$provenance = [ordered]@{
    schemaVersion = 1
    generatedAt = $generatedAt
    repository = "https://github.com/MirrorPulse/CfSharp"
    commit = $commit
    branch = $branch
    sdk = $sdkVersion
    packageSource = "https://api.nuget.org/v3/index.json"
    packageSourceMapping = "nuget.org/*"
    restore = "dotnet restore CfSharp.sln --locked-mode"
    policy = [ordered]@{
        nugetAudit = "all"
        minimumAdvisorySeverity = "moderate"
        license = "Apache-2.0"
        attribution = "MirrorPulse Team"
        secretsIncluded = $false
    }
    inputFiles = @($fileEvidence)
}

$inventoryDocument = [ordered]@{
    schemaVersion = 1
    generatedAt = $generatedAt
    commit = $commit
    projects = @($inventoryReports)
}
$vulnerabilityDocument = [ordered]@{
    schemaVersion = 1
    generatedAt = $generatedAt
    commit = $commit
    auditMode = "all"
    minimumSeverity = "moderate"
    sources = @("https://api.nuget.org/v3/index.json")
    projects = @($vulnerabilityReports)
}

$inventoryDocument | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $artifactRoot "dependency-inventory.json")
$vulnerabilityDocument | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $artifactRoot "vulnerability-audit.json")
$spdx | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $artifactRoot "sbom.spdx.json")
$provenance | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $artifactRoot "provenance.json")

$artifactText = Get-ChildItem -LiteralPath $artifactRoot -File |
    Get-Content -Raw |
    Out-String
if ($artifactText -match '(?i)(password|secret|token)\s*[:=]\s*[^\r\n]{8,}')
{
    throw "Dependency evidence appears to contain a secret-like value."
}

Write-Output "Dependency audit passed for $($projectRoots.Count) projects and $($packages.Count) packages."
