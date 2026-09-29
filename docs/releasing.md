# Release and branch model

CfSharp keeps only two long-lived branches:

- `main` is the stable line. It accepts pull requests from `develop` only, requires the
  repository CI and branch-policy checks, and does not accept direct pushes.
- `develop` is the integration line. It may receive direct development pushes and pull requests.
  The preview workflow is started manually from this branch.

Short-lived feature or fix branches are created from `develop` and are removed after their pull
request is merged. A pull request targeting `main` must come from the repository's `develop` branch
and carry exactly one release label:

| Label | Version change |
| --- | --- |
| `breaking` | increment major, reset minor and patch |
| `feature` | increment minor, reset patch |
| `fix` | increment patch |

The release workflows query the public NuGet flat-container index for all three CfSharp packages
before calculating a version. Package projects keep `VersionPrefix` at `0.0.0`; every `dotnet pack`
invocation supplies an explicit version. This prevents a stale project-file version from becoming a
published package version.

## Preview releases

Run **Preview release** manually with `develop` selected and type `PUBLISH` in the confirmation
field. The workflow increments the current NuGet preview counter, runs the Release/API/trim/AOT and
test gates, prepares the three packages and symbols, publishes them through NuGet trusted publishing,
and creates a prerelease tag and GitHub release. The first run starts at `0.1.0-preview.1` when no
CfSharp package exists on NuGet.org.

Before the first run, configure the NuGet trusted-publishing policy with:

- Repository owner: `mirrorpulse`;
- Repository: `cfsharp`;
- Workflow file: `preview.yml`;
- Environment: `preview`;
- GitHub Actions secret `NUGET_USER`: the NuGet profile name, not an email address.

The workflow requests a short-lived OIDC credential; no long-lived NuGet API key is stored in the
repository.

## Stable releases

When a `develop` pull request is merged into `main`, the stable workflow starts automatically. The
`stable` environment remains the approval gate for the stable publication policy. After approval, it
calculates the next stable version from NuGet, runs the full release gates, publishes all three
packages, creates the matching `v<version>` tag, and creates the GitHub release.

Configure a second NuGet trusted-publishing policy with workflow file `release.yml` and environment
`stable`, using the same `NUGET_USER` secret. The first `breaking` release from an empty NuGet
namespace calculates `1.0.0`.

## Version and artifact rules

- `CfSharp.Native`, `CfSharp`, and `CfSharp.Storage.Sqlite` always share one calculated version.
- Preview and stable workflows retain package, symbol, manifest, support-matrix, SBOM, test, and
  clean-consumer evidence as GitHub artifacts.
- The sample MSIX is a sample host rather than a distributable product; formal MSIX release signing
  is not a CfSharp publication gate.
- A separate release-candidate channel and post-release observation program are intentionally not
  part of the current release model.
