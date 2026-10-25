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

The plain-list experiment bypasses MainPage, quota controls, provider reads and tray integration. It retains MAUI resources and Windows app/notification initialization. The temporary startup branches were removed from production source after measurement. An ignored copy is at `artifacts/native-qa/App.performance-probe.cs`.

Toolchain: SDK 10.0.403, MAUI Controls 10.0.120, Windows App SDK 1.8.260529003 / WinUI 1.8.260204000, self-contained .NET 10.0.13. Microsoft dotnet-trace 10.0.750501 was installed only under ignored `artifacts/diagnostics-tools`.

Managed traces `artifacts/native-qa/editor.nettrace` and `static.nettrace` contain WinUI automation-peer traversal and COM wrapper/finalizer activity. The dashboard sample did not identify a Radar render/measure loop. Sampled managed stacks also include waiting threads and do not resolve the substantial native portion of the UI thread; their percentages must not be presented as CPU attribution.

Next evidence needed: native CPU stacks and a comparison without an active automation listener or on a clean user/session. Keep the plain-list reproduction available when evaluating framework servicing. Do not hide controls from accessibility to make a performance measurement pass. The earlier `artifacts/idle-test/idle-result.json` pass is historical and does not supersede this regression.
