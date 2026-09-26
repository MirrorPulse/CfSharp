# Installation and prerequisites

The first CfSharp preview is built from source while package publication is being prepared. This
page records the runtime assumptions that an application must satisfy.

## Supported runtime

- Windows 10 version 1709 (build 16299) or later exposes the core Cloud Files API.
- Rich status, provider progress, and newer placeholder operations are capability-gated by Windows
  build and Cloud Files integration number. A capability that is unavailable is reported instead
  of being silently emulated.
- x64 and ARM64 are the current target architectures. x86 is intentionally outside the stable
  support matrix and must not be added to a package or deployment by accident.
- The projects target `net10.0-windows`; the sample package uses a Windows 10 platform target for
  its desktop host.

## Build requirements

Install the .NET 10 SDK and, for native ABI verification, the Microsoft Visual C++ Build Tools.
The SDK version is pinned by `global.json`. Use a real supported Windows machine for Cloud Files
integration, Explorer, trim/AOT, and ABI checks.

## State and paths

Choose an absolute state database path outside the managed sync root, for example:

```text
C:\ProgramData\ExampleProvider\Accounts\account-42\cfsharp.db
```

Do not put content, authentication secrets, or provider-specific remote business data in the
CfSharp state store. Keep the database and the sync root under the same account's ownership policy,
but do not make one a child of the other.

## Package roles

Reference `CfSharp` for the high-level API. Reference `CfSharp.Native` only when an advanced path
requires direct native coverage. Add `CfSharp.Storage.Sqlite` when the official SQLite state store
is appropriate; custom implementations can satisfy the core state interfaces without taking a
SQLite dependency.
