# AI Reset Radar

A native Windows .NET MAUI desktop monitor for AI usage windows and expiring earned resets. The selected dark design is preserved in `design/references/approved-ui.png`.

## Current implementation

- Per-account quota rows with Used/Left text inside taller bars, lavender normal states and amber warning states.
- Chronological automatic-reset and earned-credit-expiry timeline; independent quota windows and accounts.
- Codex app-server connection using separate app-owned profiles and OS keyring storage; read-only monitoring transport.
- GitHub Copilot billing connection using a fine-grained token and current personal or organization billing endpoints. Monthly allowance is optional and explicitly user-entered.
- SQLite metadata, last-known snapshots, settings and alert history; GitHub secrets protected with current-user Windows DPAPI.
- Tray quick view/menu, close-to-tray, taskbar status overlays, optional sign-in startup, refresh/backoff, native Windows notifications and in-app banners.
- Quiet hours, snooze, privacy mode, per-account/window alert rules and thresholds, and notification test.

This is an active implementation. Live authenticated provider coverage, full multi-account renewal testing and release acceptance are not yet complete. A native CPU regression remains under investigation; see `docs/native-performance.md`. See `docs/verification.md` and the task checklist for evidence. Demo data is always labeled; it does not prove a live integration works.

## Run and build

Requires Windows and the pinned .NET 10 SDK with the `maui-windows` workload for building. The self-contained publish output is intended to run without a separately installed .NET runtime.

```powershell
dotnet build src/AIResetRadar.App/AIResetRadar.App.csproj
dotnet test AIResetRadar.slnx
dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish/win-x64
```

Run the published `artifacts/publish/win-x64/AIResetRadar.App.exe`. For a design preview, use `--demo`; `--compact` starts an 800×700 logical-pixel window. The main window also fits within the current monitor's working area. Test data can be isolated with `--data-dir <absolute-folder>`.

Preview-only error cases use `--demo --scenario unknown`, `offline`, `auth`, `permission`, `exhausted`, `due`, `partial` or `reserve-only`. Demo account/settings changes, alert history and snooze changes stay in memory. The explicit notification test still asks Windows to display a test popup.

`./scripts/Build-Windows.ps1 -Package` tests, publishes and creates an unsigned preview ZIP with a SHA-256 manifest under `artifacts/releases/`. Use `-PublishDirectory artifacts/publish/validation-win-x64` to keep a running older review copy untouched. Extract the complete ZIP before launching the executable. The ZIP is a portable review package; clean-user acceptance and installer/signing remain open. The build requires the exact SDK in `global.json`; see `docs/toolchain.md` for the supported runtime pin.

The app initially opens without accounts. **Add account** offers OpenAI Codex or GitHub Copilot. OpenAI sign-in happens in your own browser. GitHub's default **Sign in with GitHub** shows a short code and opens GitHub for approval. The developer must configure the one-time app registration described in `docs/github-sign-in.md`; normal users do not create tokens. Credentials are protected with Windows DPAPI and renewed when needed. **Use an access token instead** retains advanced personal/organization connections through a masked field. Personal billing requires Plan read permission; organization-funded usage needs authorized organization billing access. Removing a connection deletes its local credentials; GitHub App authorization can also be revoked in GitHub settings. GitHub usage may lag, and a usage report alone does not establish your allowance. General ChatGPT model quotas and OpenAI API billing are separate products and are not included.

The tray icon's menu opens Radar, refreshes usage or exits. Closing the main window keeps the monitor running when close-to-tray is enabled. **Exit Radar** ends monitoring. Startup at Windows sign-in is off by default. Windows may place tray icons in its overflow menu. Native popup appearance and delivery depend on Windows settings and Focus Assist; history remains available in Radar.

## Data and credentials

Default application state: `%LOCALAPPDATA%\AIResetRadar`. SQLite stores non-secret connection metadata and usage/alert data. Protected token files are in `Credentials`; independent Codex profiles are in `Profiles`. Existing Codex/IDE profiles are not modified. Removing a connection deletes/signs out Radar's own saved credentials only. Logs must never include tokens or provider response bodies.

Reconnect validates a separate protected credential/profile before adopting it. It keeps the account's paused state and logical identity. Superseded credential references are queued durably for cleanup; a pending cleanup is shown and retried while monitoring. Removing a connection commits that removal before retiring its credentials/history. Snapshot and alert history retain 30 days by default.

Radar does not make inference requests, redeem resets, purchase credits or switch accounts in other applications. The Codex transport has an explicit method allowlist.

## Design and plan

- `design/selection.md`: final visual selection and required details.
- `tasks/plan.md`: complete requirements and verification gates.
- `tasks/todo.md`: implementation checklist.
- `docs/toolchain.md`: verified environment.
- `docs/verification.md`: current evidence and remaining checks.

## Assets

The radar app icon was generated using the built-in ImageGen tool, based on the approved screenshot, and saved at `src/AIResetRadar.App/Resources/Images/radar.png`. A Windows ICO format conversion is in `Resources/app.ico`. OpenAI's source icon was reused from the user's CreatorMicroModelKeys project. GitHub's icon comes from its official `github.githubassets.com/favicons/favicon-dark.svg` asset. Provider marks identify the services; Radar is an independent application.

Refresh/settings toolbar assets use the official Lucide library. Its license is included in `src/AIResetRadar.App/Resources/LUCIDE-LICENSE.txt` and the published output.
