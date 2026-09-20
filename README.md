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

## Author

Quaternion8192

## License

CfSharp is licensed under the [Apache License 2.0](LICENSE).
