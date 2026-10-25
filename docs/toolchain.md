# Windows toolchain

Verified October 25, 2026:

- Windows x64 host. SDK 10.0.402 was present initially; a later authoritative inventory showed it replaced by 10.0.403. The latter is now pinned exactly in `global.json`, with roll-forward disabled.
- `maui-windows` workload installed: 10.0.20/10.0.100, SDK workload set 10.0.400.
- The initial app matched Creator Micro's MAUI 10.0.20. It now restores/builds with **Microsoft.Maui.Controls 10.0.120**, the current servicing release in Microsoft's [MAUI support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui). The Windows resource/build workload remains the installed 10.0.20 pack; the actual newer-controls Release publish succeeds. Native regression verification of that servicing change is still required.
- `Directory.Build.targets` pins .NET runtime framework resolution to **10.0.13**, the current published .NET 10 runtime in Microsoft's [support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). SDK 10.0.403's default resolution requested an unavailable ASP.NET runtime 10.0.14. The targeted .NET framework override leaves Windows SDK reference resolution intact. Published runtime configuration confirms Microsoft.NETCore.App 10.0.13.
- Windows target SDK: 10.0.19041.0; min platform 10.0.17763.0. Initial supported product scope remains Windows 11 x64. No mobile workload or global SDK installation was performed by this project run.
- The workload inventory needs a normal host process; the restricted process cannot query Service Control Manager. The authorized host inventory succeeded.
- Installed Codex executable: 0.164.0-alpha.11.1. Schema generated successfully into ignored `artifacts/codex-schema/` without loading accounts or making inference requests.
- The actual generated schema includes nullable earned-reset count/details and multiple rate-limit buckets. `account/read` identifies email and plan but does not by itself prove workspace identity. Scope verification remains required.

Test/build/runtime outcomes are tracked separately as implementation proceeds.
