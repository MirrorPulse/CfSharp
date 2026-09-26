[CmdletBinding()]
param(
    [ValidateRange(1, 100)]
    [int] $Iterations = 100,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$testProject = Join-Path $root 'tests/CfSharp.Storage.Sqlite.Tests/CfSharp.Storage.Sqlite.Tests.csproj'
$env:CFSHARP_CRASH_RECOVERY_ITERATIONS = $Iterations.ToString(
    [System.Globalization.CultureInfo]::InvariantCulture)

try {
    & dotnet test $testProject `
        --configuration $Configuration `
        --no-restore `
        -p:TargetPlatformDisplayName=Windows `
        --filter 'FullyQualifiedName~AbruptProcessExitPreservesOnlyDurableWritesAcrossWalRecovery' `
        --logger 'trx;LogFileName=crash-recovery.trx'
    if ($LASTEXITCODE -ne 0) {
        throw "Crash recovery matrix failed with exit code $LASTEXITCODE."
    }

    Write-Output "Crash/WAL recovery matrix passed: $Iterations iteration(s)."
}
finally {
    Remove-Item Env:CFSHARP_CRASH_RECOVERY_ITERATIONS -ErrorAction SilentlyContinue
}
