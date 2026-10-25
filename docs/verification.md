# Implementation evidence and remaining gates

Updated October 25, 2026. The build goal remains active. Fixtures and builds do not substitute for authenticated account or native UI acceptance.

## Verified

- **51 passing Windows tests:** 30 Core and 21 Infrastructure, zero skipped/failed. Coverage includes quota math/unknowns/overage, reset confirmation, DST, partial reserves, alert crossings and scoped rules, expiry keys, malformed values, identity mismatches, organization-user filtering, 403/429 handling, body bounds, DPAPI, snapshot retention on collection, schema migration/future-version rejection, and atomic rollback of account/snapshot/retired-credential references.
- Native self-contained Release x64 publish succeeds with MAUI Controls **10.0.120**, exact SDK **10.0.403**, and the pinned .NET **10.0.13** runtime. Published runtime configuration and restored assets were inspected. The installed Windows build workload remains 10.0.20. See `toolchain.md` for the SDK replacement and framework-resolution decision.
- NuGet audit across the solution and transitive dependencies reports no known vulnerable packages from configured sources.
- Installed Codex schema was generated with 0.164.0-alpha.11.1. Optional workspace, multiple quota buckets, earned resets and included-usage permission are represented without inventing absent values.
- The real stdio probe was rerun after transport-disposal changes: two separate empty app-owned profiles returned no account; inference was rejected by the allowlist. Afterwards the authoritative host inventory found zero children matching the monitor's minimal app-server signature. This proves empty-profile separation and probe shutdown, not authenticated renewal isolation.
- A second-model code review identified stale reconnect results and destructive-before-commit removal. Fixes use immutable account generations, staged credential IDs, durable cleanup references and atomic metadata adoption/removal. Settings resolve the latest account record. Lifecycle/secret-preservation/DB rollback tests pass; full native interleaving and fault scenarios remain open.

## Native observations already obtained

- Real Windows Computer Use observed the dashboard, four demo rows, timeline, Settings and accessibility labels. References: `design/references/approved-ui.png` and CreatorMicroModelKeys editor/settings screenshots.
- Compact startup requested **800x700**; the captured client was approximately **789x694**. Rows reflowed and the timeline scrolled horizontally. Choosing **Least remaining** put the 18%-remaining Copilot row first.
- A native click on **Send test notification** persisted one event; Windows returned a nonzero notification ID. A separate store read confirmed one Windows-accepted event. This does not prove a visible popup under Focus Assist.
- Initial startup exceeded the working area; a work-area clamp was added. Final desktop sizing/scaling needs a new observation.
- Repeated size-change reconstruction caused excessive idle CPU. Rendering now changes only for width/breakpoint changes. The bounded regression run recorded **0.0 CPU seconds over 15 seconds**, with working set **261.8 -> 259.2 MB**. Evidence: ignored `artifacts/idle-test/idle-result.json`; repeat with `scripts/Test-Idle.ps1`. That sample predates servicing and the newest controls; their final resource measurement remains required.
- Computer Use reported a physical **Escape** stop. UI checks remain stopped pending the user's answer to the resume question. Auth dialogs are not automated.

## Distribution

`scripts/Build-Windows.ps1 -Package` provides the test/publish/package recipe. `scripts/Package-Windows.ps1` rejects obvious local-state/credential files, checks required self-contained files in the ZIP, and writes a SHA-256 manifest. Current preview input is `artifacts/publish/validation-win-x64`, preserving the running older review copy. The ZIP is unsigned; it is not a completed installer or clean-user acceptance result.

## Required open gates

- Authenticated Codex usage/window/reserve comparison; two independent signed-in accounts, renewal, restart, rejected reconnect and removal isolation, including the existing external Codex/IDE login.
- Authenticated GitHub personal/organization access, allowance/discount/overage reconciliation and billing latency. Official billing endpoint documentation was rechecked; actual account coverage remains absent.
- Final desktop/compact comparison after servicing and rule changes, keyboard/Narrator, 100/150/200% DPI, empty/error/partial states and final resource/soak measurements.
- Tray quick view/positioning, hide/restore/Exit, Explorer restart, startup, network/power/clock recovery and child shutdown during real sessions.
- Notification View/Snooze/Dismiss, exact-window/cold activation, OS-disabled notifications, quiet hours, restart deduplication and persistent-delivery interleavings.
- Transport/process/DB fault matrix, cleanup-retry failures, retention independent of successful collection, provider capability/usage-page affordances and the final requirement audit.
- Clean-user extraction/launch/notifications/removal, final packaging/signing decision and README walkthrough.

No live check is passed because a demo, build, schema or empty-profile probe succeeds. No credential contents were copied into evidence.
