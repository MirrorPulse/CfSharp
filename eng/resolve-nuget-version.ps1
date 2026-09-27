[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Stable', 'Preview')]
    [string] $Channel,
    [ValidateSet('Breaking', 'Feature', 'Fix')]
    [string] $Bump = 'Fix',
    [string[]] $PackageId = @('CfSharp.Native', 'CfSharp', 'CfSharp.Storage.Sqlite'),
    [string] $VersionsFile,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

function ConvertTo-SemanticVersion {
    param([AllowNull()][string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $match = [regex]::Match(
        $Value.Trim(),
        '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-preview\.(?<preview>\d+))?$')
    if (-not $match.Success) {
        return $null
    }

    [pscustomobject]@{
        Major = [int64]$match.Groups['major'].Value
        Minor = [int64]$match.Groups['minor'].Value
        Patch = [int64]$match.Groups['patch'].Value
        Preview = if ($match.Groups['preview'].Success) {
            [int64]$match.Groups['preview'].Value
        } else {
            $null
        }
        Text = $Value.Trim()
    }
}

function Compare-BaseVersion {
    param($Left, $Right)

    foreach ($property in @('Major', 'Minor', 'Patch')) {
        if ($Left.$property -lt $Right.$property) {
            return -1
        }
        if ($Left.$property -gt $Right.$property) {
            return 1
        }
    }

    return 0
}

function Get-VersionMap {
    param([string] $FixturePath)

    if (-not [string]::IsNullOrWhiteSpace($FixturePath)) {
        $fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
        $map = @{}
        foreach ($id in $PackageId) {
            $property = $fixture.PSObject.Properties[$id]
            $map[$id] = if ($null -eq $property) {
                @()
            } else {
                @($property.Value | ForEach-Object { [string]$_ })
            }
        }
        return $map
    }

    $map = @{}
    foreach ($id in $PackageId) {
        $flatContainerId = $id.ToLowerInvariant()
        $uri = "https://api.nuget.org/v3-flatcontainer/$flatContainerId/index.json"
        try {
            $response = Invoke-RestMethod -Uri $uri -Method Get
            $map[$id] = @($response.versions | ForEach-Object { [string]$_ })
        } catch {
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 404) {
                $map[$id] = @()
                continue
            }
            throw "Unable to query NuGet versions for '$id' from '$uri': $($_.Exception.Message)"
        }
    }

    return $map
}

function Format-BaseVersion {
    param($Version)

    return "$($Version.Major).$($Version.Minor).$($Version.Patch)"
}

function Get-NextStableVersion {
    param($LatestStable)

    $base = if ($null -eq $LatestStable) {
        [pscustomobject]@{ Major = [int64]0; Minor = [int64]0; Patch = [int64]0 }
    } else {
        $LatestStable
    }

    switch ($Bump) {
        'Breaking' {
            return "$($base.Major + 1).0.0"
        }
        'Feature' {
            return "$($base.Major).$($base.Minor + 1).0"
        }
        'Fix' {
            return "$($base.Major).$($base.Minor).$($base.Patch + 1)"
        }
    }
}

$versionMap = Get-VersionMap -FixturePath $VersionsFile
$observed = foreach ($id in $PackageId) {
    foreach ($version in $versionMap[$id]) {
        $parsed = ConvertTo-SemanticVersion $version
        if ($null -ne $parsed) {
            [pscustomobject]@{
                PackageId = $id
                Major = $parsed.Major
                Minor = $parsed.Minor
                Patch = $parsed.Patch
                Preview = $parsed.Preview
                Text = $parsed.Text
            }
        }
    }
}

$stableVersions = @($observed | Where-Object { $null -eq $_.Preview })
$latestStable = $stableVersions |
    Sort-Object Major, Minor, Patch -Descending |
    Select-Object -First 1

if ($Channel -eq 'Stable') {
    $version = Get-NextStableVersion -LatestStable $latestStable
} else {
    $previewVersions = @($observed | Where-Object { $null -ne $_.Preview })
    $latestPreview = $previewVersions |
        Sort-Object Major, Minor, Patch, Preview -Descending |
        Select-Object -First 1

    $stableBase = if ($null -eq $latestStable) {
        [pscustomobject]@{ Major = [int64]0; Minor = [int64]0; Patch = [int64]0 }
    } else {
        $latestStable
    }

    if ($null -ne $latestPreview -and (Compare-BaseVersion $latestPreview $stableBase) -gt 0) {
        $version = "$(Format-BaseVersion $latestPreview)-preview.$($latestPreview.Preview + 1)"
    } else {
        $version = "$($stableBase.Major).$($stableBase.Minor + 1).0-preview.1"
    }
}

$result = [ordered]@{
    channel = $Channel.ToLowerInvariant()
    bump = $Bump.ToLowerInvariant()
    version = $version
    source = if ([string]::IsNullOrWhiteSpace($VersionsFile)) { 'nuget.org' } else { 'fixture' }
    packages = $PackageId
    latestStable = if ($null -eq $latestStable) { $null } else { $latestStable.Text }
    observedVersions = [ordered]@{}
}
foreach ($id in $PackageId) {
    $result.observedVersions[$id] = @($versionMap[$id])
}

$json = $result | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $json | Set-Content -LiteralPath $OutputPath -Encoding utf8
}
Write-Output $json
