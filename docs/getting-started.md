# Getting started

This guide builds CfSharp from source and runs the local sample provider. It assumes a supported
64-bit Windows installation and the .NET 10 SDK.

## 1. Clone and verify the toolchain

```powershell
git clone https://github.com/MirrorPulse/CfSharp.git
cd CfSharp
dotnet --info
```

Use the SDK selected by `global.json`. The Cloud Files API is Windows-only; do not use a Linux or
macOS environment as a substitute for the integration and ABI checks.

## 2. Build and test

```powershell
dotnet restore CfSharp.sln
dotnet build CfSharp.sln --configuration Release --no-restore
dotnet test CfSharp.sln --configuration Release --no-build
```

The native ABI probe is an additional x64 check and requires the Microsoft Visual C++ Build Tools.
The standard test command intentionally does not replace that probe.

## 3. Run the sample provider

Create separate directories for source content, the managed sync root, and durable state:

```powershell
New-Item -ItemType Directory -Force C:\CloudContent, C:\CloudSyncRoot, C:\CloudState | Out-Null
Set-Content -Path C:\CloudContent\hello.txt -Value 'Hello from CfSharp'

dotnet run --project samples/CfSharp.SampleProvider -- `
  run C:\CloudContent `
  C:\CloudSyncRoot `
  --state-db C:\CloudState\cfsharp.db `
  --once
```

The sample owns process-scoped registration work, then exits. It intentionally leaves the Windows
sync-root registration installed; remove it only as an explicit account-removal operation.

## 4. Inspect the result

Open the sync root in Explorer and read the placeholder. The first read may trigger a demand
callback and hydrate content from the sample's local source directory. The state database contains
coordination metadata only; it never becomes a content cache or a credential store.

## Next steps

- Read [sync-root lifecycle](sync-root-lifecycle.md) before adding account or uninstall flows.
- Read [SQLite state](state-store-sqlite.md) before choosing a database location.
- Read [placeholders and hydration](placeholders.md) before implementing a content provider.
