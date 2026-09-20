# CfSharp.Native ABI Probe

This native executable reports ABI facts directly from the active Windows SDK headers. Managed ABI tests will compare its output with `CfSharp.Native` layouts and constants.

The probe requires the Microsoft Visual C++ Build Tools and CMake. It is intentionally excluded from the default .NET solution until the native toolchain is part of the documented development prerequisites.

Example configuration from a Visual Studio developer shell:

```powershell
cmake -S tests/CfSharp.Native.AbiProbe -B artifacts/abi-probe -A x64
cmake --build artifacts/abi-probe --config Release
```
