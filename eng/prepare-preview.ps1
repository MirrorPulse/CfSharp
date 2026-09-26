[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+-preview\.\d+$')]
    [string] $Version = '0.1.0-preview.1',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\preview')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $root $OutputDirectory
}
$output = [System.IO.Path]::GetFullPath($outputPath)
$versionOutput = Join-Path $output $Version
$packageOutput = Join-Path $versionOutput 'packages'
$smokeRoot = Join-Path $versionOutput 'consumer-smoke'

New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null

$projects = @(
    'src/CfSharp.Native/CfSharp.Native.csproj',
    'src/CfSharp/CfSharp.csproj',
    'src/CfSharp.Storage.Sqlite/CfSharp.Storage.Sqlite.csproj'
)
foreach ($relativeProject in $projects) {
    $project = Join-Path $root $relativeProject
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Preview project is missing: $relativeProject"
    }

    & dotnet pack $project `
        --configuration $Configuration `
        --no-restore `
        --output $packageOutput `
        -p:TargetPlatformDisplayName=Windows `
        /p:Version=$Version `
        /p:PackageVersion=$Version `
        /p:IncludeSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "Preview pack failed for $relativeProject with exit code $LASTEXITCODE."
    }
}

$packageFiles = @(Get-ChildItem -LiteralPath $packageOutput -Filter '*.nupkg' -File)
if ($packageFiles.Count -ne $projects.Count) {
    throw "Expected $($projects.Count) preview packages, found $($packageFiles.Count)."
}

$packageEvidence = foreach ($package in $packageFiles | Sort-Object Name) {
    $escapedVersion = [regex]::Escape($Version)
    if ($package.Name -notmatch "\.$escapedVersion\.nupkg$") {
        throw "Package has an unexpected preview version: $($package.Name)"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $nuspecEntry = $archive.Entries |
            Where-Object { $_.FullName -like '*.nuspec' } |
            Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw "Package has no nuspec: $($package.Name)"
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.package.metadata
        if ($metadata.authors -ne 'MirrorPulse Team' -or
            $metadata.license.InnerText -ne 'Apache-2.0') {
            throw "Package metadata attribution/license is invalid: $($package.Name)"
        }

        [pscustomobject]@{
            id = [string]$metadata.id
            version = [string]$metadata.version
            file = $package.Name
            bytes = $package.Length
            sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
            entries = @($archive.Entries | ForEach-Object FullName)
        }
    }
    finally {
        $archive.Dispose()
    }
}

if (Test-Path -LiteralPath $smokeRoot) {
    Remove-Item -LiteralPath $smokeRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
$consumerProject = Join-Path $smokeRoot 'PreviewConsumer.csproj'
$consumerProgram = Join-Path $smokeRoot 'Program.cs'
$consumerNuGetConfig = Join-Path $smokeRoot 'NuGet.config'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestorePackagesWithLockFile>false</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Remove="Microsoft.DotNet.ILCompiler;Microsoft.NET.ILLink.Tasks" />
  </ItemGroup>
  <ItemGroup>
    <PackageVersion Include="CfSharp" Version="$Version" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CfSharp" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $consumerProject -Encoding utf8
@"
using CfSharp;

Console.WriteLine(new CloudFilesPlatformInfo(26100, 0, 1536).Supports(
    CloudFilesCapability.PlaceholderRangeInfoForHydration));
"@ | Set-Content -LiteralPath $consumerProgram -Encoding utf8
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="preview" value="$packageOutput" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="preview">
      <package pattern="CfSharp*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $consumerNuGetConfig -Encoding utf8

$savedUserProfile = $env:USERPROFILE
$savedAppData = $env:APPDATA
$savedDotnetCliHome = $env:DOTNET_CLI_HOME
$consumerUserProfile = Join-Path $versionOutput '.consumer-user'
$env:USERPROFILE = $consumerUserProfile
$env:APPDATA = Join-Path $consumerUserProfile 'AppData/Roaming'
$env:DOTNET_CLI_HOME = Join-Path $consumerUserProfile '.dotnet'
New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:DOTNET_CLI_HOME | Out-Null
try {
    & dotnet restore $consumerProject --configfile $consumerNuGetConfig --ignore-failed-sources `
        -p:TargetPlatformDisplayName=Windows
    if ($LASTEXITCODE -ne 0) {
        throw "Preview consumer restore failed with exit code $LASTEXITCODE."
    }
    & dotnet build $consumerProject --configuration $Configuration --no-restore `
        -p:TargetPlatformDisplayName=Windows
    if ($LASTEXITCODE -ne 0) {
        throw "Preview consumer build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:USERPROFILE = $savedUserProfile
    $env:APPDATA = $savedAppData
    $env:DOTNET_CLI_HOME = $savedDotnetCliHome
}

$packageProject = Join-Path $root 'samples/CfSharp.SampleProvider.Package/CfSharp.SampleProvider.Package.wapproj'
[xml] $packageProjectXml = Get-Content -LiteralPath $packageProject -Raw
$platforms = [string]$packageProjectXml.Project.PropertyGroup.Platforms
$minimumVersion = [string]$packageProjectXml.Project.PropertyGroup.TargetPlatformMinVersion

$manifest = [pscustomobject]@{
    version = $Version
    configuration = $Configuration
    repositoryCommit = (git -C $root rev-parse HEAD).Trim()
    packages = @($packageEvidence)
    consumerSmoke = 'passed'
    msix = [pscustomobject]@{
        project = 'samples/CfSharp.SampleProvider.Package/CfSharp.SampleProvider.Package.wapproj'
        platforms = $platforms -split ';'
        targetPlatformMinVersion = $minimumVersion
        signing = 'manual-preview-step-required'
        build = 'not-invoked'
    }
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $versionOutput 'manifest.json')
Write-Output "Preview dry-run completed: $versionOutput"
