# Security policy

CfSharp exposes Windows Cloud Files APIs and persists provider coordination state. A security
issue can therefore affect native interop, file-system state, callback handling, package supply
chain, or release automation.

## Supported versions

Only the current `main` branch and the latest preview published from `develop` receive security
fixes. Older preview packages, detached commits, and private forks are not supported security
targets. CfSharp currently supports Windows 10 build 16299 or later on x64 and ARM64; reports
against unsupported operating systems or x86 are still welcome, but may not receive a fix.

## Reporting a vulnerability

Do not open a public issue, pull request, or discussion for an undisclosed vulnerability. Use a
private GitHub Security Advisory for `MirrorPulse/CfSharp`. If private reporting is unavailable,
contact the repository maintainer privately through the GitHub account listed in
[CODEOWNERS](.github/CODEOWNERS), and do not include exploit details in a public channel.

Please include, when safe to share:

- the affected commit, package version, or workflow;
- Windows version/build, architecture, .NET SDK/runtime version, and deployment mode;
- a minimal reproduction or proof of concept that does not access real user data;
- expected and observed behavior, including native error codes or HRESULTs;
- whether credentials, personal data, sync-root contents, or release artifacts may be exposed.

Redact tokens, private keys, user data, and production paths before sending a report. Do not test
against another person's sync root or attempt to access repository secrets.

## Response and disclosure

Maintainers aim to acknowledge a report within five business days and will provide a status update
when triage is complete. The schedule for a fix depends on severity, reproducibility, upstream
Windows behavior, and the availability of a safe regression test. Please coordinate public
disclosure with the maintainers; a fix may require a patched package, workflow change, or Windows
compatibility note.

Security fixes follow the normal atomic-commit and review requirements in
[CONTRIBUTING.md](CONTRIBUTING.md), with additional private review when the report is not yet
public. Do not include the report, exploit, credentials, or private evidence in the public commit
history.

## Supply-chain and release reports

Report suspicious package contents, dependency resolution, OIDC/Trusted Publishing behavior,
workflow permissions, protected-branch bypasses, or leaked release artifacts as security issues.
Do not attempt to publish packages or tags while investigating. The release model and manual
approval boundaries are documented in [docs/releasing.md](docs/releasing.md).
