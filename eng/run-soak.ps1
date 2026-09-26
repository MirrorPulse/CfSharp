[CmdletBinding()]
param(
    [ValidateRange(1, 1440)]
    [int] $DurationMinutes = 30,
    [ValidateRange(0, 100000)]
    [int] $MaxIterations = 0,
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

$deadline = [DateTimeOffset]::UtcNow.AddMinutes($DurationMinutes)
$iteration = 0
$firstRun = $true
$results = [System.Collections.Generic.List[object]]::new()

try {
    while ([DateTimeOffset]::UtcNow -lt $deadline -and
        ($MaxIterations -eq 0 -or $iteration -lt $MaxIterations)) {
        $iteration++
        $started = [DateTimeOffset]::UtcNow
        $arguments = @(
            'test',
            'tests/CfSharp.Tests/CfSharp.Tests.csproj',
            '--configuration', $Configuration,
            '--no-restore',
            '--filter', 'Category=Soak',
            '--logger', "trx;LogFileName=soak-$iteration.trx",
            '--results-directory', $output
        )
        if (-not $firstRun) {
            $arguments += '--no-build'
        }

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Soak iteration $iteration failed with exit code $LASTEXITCODE."
        }

        $firstRun = $false
        $finished = [DateTimeOffset]::UtcNow
        $results.Add([pscustomobject]@{
                iteration = $iteration
                startedUtc = $started.ToString('O')
                finishedUtc = $finished.ToString('O')
                durationSeconds = [math]::Round(($finished - $started).TotalSeconds, 3)
                status = 'passed'
            })
        Write-Output "Soak iteration $iteration passed."
    }
}
finally {
    $summary = [pscustomobject]@{
        durationMinutes = $DurationMinutes
        iterations = $results.Count
        completedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        results = $results
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'summary.json')
}

if ($results.Count -eq 0) {
    throw 'The soak completed without running an iteration.'
}

Write-Output "Soak completed: $($results.Count) iteration(s) in a $DurationMinutes-minute budget."
