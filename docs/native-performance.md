# Native performance investigation

October 25, 2026. Resource acceptance remains open.

The current native session showed sluggish editing and sustained CPU use. A fresh launch reproduced the load before targeted UI inspection. Closing modal pages and resetting the inspection JavaScript session did not resolve it. These observations do not establish whether an external accessibility listener contributes.

| Controlled variant | CPU seconds | Sample seconds | Working set |
|---|---:|---:|---:|
| Dashboard after closing Settings/rules | 8.547 | 10 | 596.3 MB |
| Dashboard after inspection-client reset | 8.375 | 10 | 595.9 MB |
| Fresh demo before targeted inspection | 8.297 | 10 | 311.6 MB |
| Plain MAUI one-label window, longer warm-up | 0.969 | 15 | 211.3 MB |
| Plain MAUI list of 100 labels | 8.594 | 12 | 348.3 MB |
| Dashboard without tray/timer integration, different title | 8.641 | 12 | 311.4 MB |
| Connected OpenAI dashboard, previous validation build | 7.812 | 10 | 535.5 MB |
| Latest Release at a new executable path, before new window inspection | 3.953 | 10 | 314.6 MB |

The plain-list experiment bypasses MainPage, quota controls, provider reads and tray integration. It retains MAUI resources and Windows app/notification initialization. The temporary startup branches were removed from production source after measurement. An ignored copy is at `artifacts/native-qa/App.performance-probe.cs`.

Toolchain: SDK 10.0.403, MAUI Controls 10.0.120, Windows App SDK 1.8.260529003 / WinUI 1.8.260204000, self-contained .NET 10.0.13. Microsoft dotnet-trace 10.0.750501 was installed only under ignored `artifacts/diagnostics-tools`.

Managed traces `artifacts/native-qa/editor.nettrace` and `static.nettrace` contain WinUI automation-peer traversal and COM wrapper/finalizer activity. The dashboard sample did not identify a Radar render/measure loop. Sampled managed stacks also include waiting threads and do not resolve the substantial native portion of the UI thread; their percentages must not be presented as CPU attribution.

Next evidence needed: native CPU stacks and a comparison without an active automation listener or on a clean user/session. Keep the plain-list reproduction available when evaluating framework servicing. Do not hide controls from accessibility to make a performance measurement pass. The earlier `artifacts/idle-test/idle-result.json` pass is historical and does not supersede this regression.

The current host is not elevated; WPR reports no active recording. No system-wide trace was started. Installed WinDbg/CDB/WPA tools were not found in the checked SDK/Visual Studio locations. A local ignored `artifacts/stack-probe` utility uses Microsoft's signed DbgEng 20260319.1511.0 and ClrDebug 0.4.2. It verifies the exact owned executable path and uses noninvasive, no-suspend mode, with no memory dump or variable inspection. One hot-thread read returned only a WinUI export-offset frame and an unresolved frame; a symbol-enabled attempt exited 1 without a usable stack. Radar remained running and responsive. This is insufficient to identify the hot native function.

Microsoft's [1.8 release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8) list 1.8.12 (1.8.260921001) as newer servicing than the app's current 1.8.9. They do not establish a fix for this observed CPU issue. No unverified framework override was promoted into the app. The completed OpenAI sign-in and restart check still show elevated load; differing samples do not establish an improvement or a cause. Further native measurement remains required.

## Three-account runtime sample — October 25, 21:12–21:15 CDT

The running quick-view-access-win-x64 build was sampled six times at 30-second intervals after the native quick-view check. All samples reported responsive. Total elapsed time was 180.18 seconds and process CPU increased 4.719 seconds (about 2.62% of one CPU core averaged over the interval). Working set ranged from 422.6 to 435.5 MiB, ending 12.9 MiB above the first sample. Handle count ranged from 1528 to 1575 and ended at 1554. The last crash log remained dated 20:05:14 CDT. Raw non-secret measurements are in ignored artifacts/native-qa/three-account-runtime.csv.

This is lower CPU utilization than the earlier sustained-load reproduction, with two OpenAI accounts and GitHub connected. It does not isolate the cause, prove a leak absent, establish long-duration stability, or replace clean-session/native-stack investigation. No active UI inspection ran during this sample.
