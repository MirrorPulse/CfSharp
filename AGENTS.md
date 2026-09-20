# CfSharp Project Guide

## Project Purpose

CfSharp is a Windows-only .NET library that exposes the complete Windows Cloud Files API (`cfapi.h` / `CldApi.dll`) through a safe, idiomatic, and well-documented C# API.

## Design Principles

- Preserve complete native API coverage. No native capability may become unreachable from managed code.
- Make the high-level API the default user experience. Callers should not need to manage pointers, native unions, structure sizes, callback lifetimes, or raw `HRESULT` values.
- Maintain a strict dependency direction: `CfSharp.Native` provides accurate native bindings, `CfSharp` provides safe domain abstractions, and optional integration packages such as `CfSharp.Storage.Sqlite` depend on `CfSharp` without reversing either dependency.
- Keep `CfSharp.Native` conventional and polished even though it primarily serves advanced users and the high-level package.
- Prefer explicit lifecycle and ownership semantics over hidden global state.
- Preserve native error details whenever errors are translated into managed exceptions or results.
- Require callers to select durable state explicitly. The official SQLite implementation requires a caller-supplied path, while custom transactional stores remain fully replaceable.
- Configure state through a factory. `CloudFileSystem` opens the store only after validating the sync root and assumes disposal ownership after a successful open.
- Never place the SQLite state database inside a managed sync root or persist file content, authentication secrets, or provider-specific remote business data in the CfSharp state store.

## Language And Documentation

- Use English for all source files, identifiers, comments, public documentation, tests, examples, and project metadata.
- Document every public API with detailed XML documentation, including ownership, lifetime, thread-safety, platform requirements, failure modes, and native behavior where relevant.
- Add detailed implementation comments for interop invariants, memory layout, marshalling, callback lifetime, and non-obvious operating-system behavior.
- Do not add comments that merely restate self-explanatory code.

## Quality Requirements

- Treat ABI layout and calling-convention correctness as testable contracts.
- Test supported CPU architectures and Windows capability boundaries explicitly.
- Keep unsafe code isolated in the native layer unless a measured design constraint requires otherwise.
- Design public APIs so Native AOT compatibility remains possible.
- Do not silently reduce functionality to make the high-level API simpler; expose an advanced path when a safe abstraction cannot represent a native feature.

## Repository And Attribution

- Use Apache License 2.0. Use `MirrorPulse Team` for project-owned copyright, package author, and primary attribution metadata.
- Follow Conventional Commits with an optional focused scope.
- Keep commits atomic: one logical change or one fix per commit.
- Never mix unrelated fixes, refactoring, formatting, dependency updates, or documentation changes.
- Add tests and documentation in the same commit as the behavior they cover when they belong to that logical change.
- Keep each commit buildable and testable whenever technically possible.
- Follow `CONTRIBUTING.md` for commit types, verification, and contribution scope.
