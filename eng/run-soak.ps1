[CmdletBinding()]
param(
    [ValidateRange(1, 1440)]
    [int] $DurationMinutes = 30,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\soak')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $root $OutputDirectory
}
$output = [System.IO.Path]::GetFullPath($outputPath)
New-Item -ItemType Directory -Force -Path $output | Out-Null

$runtimeArtifact = Join-Path $output 'runtime-soak.json'
$runnerArtifact = Join-Path $output 'runner-summary.json'
$savedDuration = $env:CFSHARP_SOAK_DURATION_MINUTES
$savedArtifact = $env:CFSHARP_SOAK_ARTIFACT
$status = 'failed'
$exitCode = 1
$env:CFSHARP_SOAK_DURATION_MINUTES = $DurationMinutes.ToString(
    [System.Globalization.CultureInfo]::InvariantCulture)
$env:CFSHARP_SOAK_ARTIFACT = $runtimeArtifact

try {
    & dotnet test tests/CfSharp.Tests/CfSharp.Tests.csproj `
        --configuration $Configuration `
        --no-restore `
        --filter 'Category=LongSoak' `
        --logger 'trx;LogFileName=long-soak.trx' `
        --results-directory $output `
        --blame-hang-timeout "$($DurationMinutes + 5)m"
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Long-soak test failed with exit code $exitCode."
    }

    $status = 'passed'
    Write-Output "Continuous long-soak completed: $DurationMinutes minute(s)."
}
finally {
    [pscustomobject]@{
        status = $status
        durationMinutes = $DurationMinutes
        testCategory = 'LongSoak'
        commit = (git -C $root rev-parse HEAD).Trim()
        runtimeArtifact = [System.IO.Path]::GetFileName($runtimeArtifact)
        completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json | Set-Content -LiteralPath $runnerArtifact -Encoding utf8

    if ($null -eq $savedDuration) {
        Remove-Item Env:CFSHARP_SOAK_DURATION_MINUTES -ErrorAction SilentlyContinue
    } else {
        $env:CFSHARP_SOAK_DURATION_MINUTES = $savedDuration
    }
    if ($null -eq $savedArtifact) {
        Remove-Item Env:CFSHARP_SOAK_ARTIFACT -ErrorAction SilentlyContinue
    } else {
        $env:CFSHARP_SOAK_ARTIFACT = $savedArtifact
    }
}

if ($status -ne 'passed') {
    throw "The continuous long-soak did not pass. See $runnerArtifact and the test results."
}
