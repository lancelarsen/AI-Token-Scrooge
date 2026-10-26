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

## Icon-timeline runtime sample — October 25, 22:46–22:49 CDT

The running icon-timeline-win-x64 preview (PID 54140) was sampled seven times over 180.13 seconds, with no agent UI actions during the interval. All samples reported responsive. CPU increased 14.141 seconds, averaging 7.85% of one core. Of that total, 12.844 seconds occurred between 03:47:28 and 03:47:58 UTC (about 42.81% of one core in that interval). Other intervals were much quieter; this is not evidence of uniformly sustained high load.

Working set ranged 467.92–480.63 MiB and ended at 472.80 MiB, 7.83 below its starting value. Private bytes ended at 337.73 MiB, 11.44 below the initial value. Handles ranged 1676–1726 and ended at 1694, 21 below the start. Raw measurements are in ignored artifacts/icon-timeline-resource-sample.json.

This run does not show monotonic growth, but cannot rule out a leak. Average CPU exceeded the earlier 2.62% three-account sample; runtime duration, user activity, provider refreshes and accessibility-client attachment were not controlled equivalently. No causal attribution to the timeline or the unchanged-render optimization is justified. Correlation of the concentrated CPU interval with provider/UI/native stacks remains needed. Resource acceptance remains open.

## Correlation with persisted refresh history

A read-only query of history.observed_at (Unix milliseconds) for the exact sample interval found three observations within the concentrated CPU interval: OpenAI Pro at 03:47:37.984 UTC, GitHub Max at 03:47:38.284, and OpenAI Plus at 03:47:40.519. A later interval contained Pro at 03:49:41.153 and Plus at 03:49:42.479; that interval used only 0.844 CPU seconds versus 12.844 during the earlier all-provider interval.

Thus the burst overlaps refresh completion and is not simply continuous idle activity. This comparison does not distinguish provider processing, persistence, full dashboard rebuilding, GC, accessibility traversal or external user interaction, and the missing GitHub refresh in the later interval is not proof of a GitHub cause. A future bounded trace should encompass a refresh cycle and compare hot stacks rather than infer causation from timestamps. Only timestamp/plan/health fields were emitted; account identities and credentials were not inspected.

## Bounded managed sampling after icon timeline

Collected a 150-second trace of the verified icon-timeline process 54140 using installed dotnet-trace with dotnet-sampled-thread-time and a 32 MiB buffer. The legacy cpu-sampling profile was rejected because this version exposes kernel CPU sampling only through collect-linux. The supported Windows profile samples managed thread wall time, not CPU attribution. Trace completed normally; files remain local/ignored at artifacts/native-qa/icon-refresh.nettrace and icon-refresh-stacks.txt.

The top report was dominated by thread waits and IApplicationStaticsMethods.Start (native application loop). Relevant sampled stacks included IObjectReference.Finalize, ComWrappers NativeObjectWrapper finalization, and automation GetChildrenCore/NavigateCore. MainPage.Render appeared at 0.02% inclusive sampled thread time in the top-100 report; that does not measure its CPU share or exclude native work it triggers. No clear managed rendering loop was identified that justifies a targeted code fix. This is consistent with earlier managed traces but does not explain or reproduce the previous CPU spike. Native CPU stacks and clean-session comparison remain required for resource acceptance; do not remove accessibility to improve measurements.
