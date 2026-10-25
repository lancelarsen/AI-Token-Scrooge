# Windows toolchain

Verified October 25, 2026:

- Windows host with .NET SDK 10.0.402; pinned in `global.json`.
- `maui-windows` workload installed: 10.0.20/10.0.100, SDK workload set 10.0.400.
- Initial Windows app package uses the installed MAUI 10.0.20 controls, matching Creator Micro. Reassess a servicing update before release.
- The workload inventory needs a normal host process; the restricted process cannot query Service Control Manager. The authorized host inventory succeeded.
- Installed Codex executable: 0.164.0-alpha.11.1. Schema generated successfully into ignored `artifacts/codex-schema/` without loading accounts or making inference requests.
- The actual generated schema includes nullable earned-reset count/details and multiple rate-limit buckets. `account/read` identifies email and plan but does not by itself prove workspace identity. Scope verification remains required.

Test/build/runtime outcomes are tracked separately as implementation proceeds.
