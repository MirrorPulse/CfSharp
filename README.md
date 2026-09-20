# CfSharp

CfSharp is a Windows-only .NET library for the Windows Cloud Files API (`cfapi.h` and `CldApi.dll`).

The project is designed around two packages:

- `CfSharp.Native` provides complete, ABI-accurate native bindings.
- `CfSharp` provides a safe, idiomatic file-system API for sync providers.

## Goals

- Preserve access to every native CFAPI capability.
- Provide an elegant high-level API for files, directories, placeholders, hydration, population, and synchronization state.
- Distinguish Windows demand requests, durable local changes, and application-supplied remote changes.
- Make ownership, cancellation, partial failure, platform requirements, and native errors explicit.
- Maintain detailed public documentation and verifiable ABI compatibility.

## Status

CfSharp is in the design and repository-bootstrap stage. No production package has been released.

## Platform

CfSharp targets Windows and is intended for desktop sync-provider applications.

## Development

The repository currently uses the .NET SDK selected by `global.json`.

```powershell
dotnet restore CfSharp.sln
dotnet build CfSharp.sln --configuration Release --no-restore
dotnet test CfSharp.sln --configuration Release --no-build
dotnet pack src/CfSharp.Native/CfSharp.Native.csproj --configuration Release --no-build
dotnet pack src/CfSharp/CfSharp.csproj --configuration Release --no-build
```

The native ABI probe under `tests/CfSharp.Native.AbiProbe` additionally requires the Microsoft Visual C++ Build Tools.

## Author

Quaternion8192

## License

CfSharp is licensed under the [Apache License 2.0](LICENSE).
