<p align="center">
  <img src="src/AIResetRadar.App/Resources/Images/token_baron.png" alt="AI Token Scrooge mascot" width="120">
</p>

# AI Token Scrooge

**Never Waste a Token! Never Miss a Reset!**

Keep track of what is left across your AI subscriptions—and when it comes back. AI Token Scrooge is a native Windows desktop app that brings **OpenAI Codex, GitHub Copilot, and Claude** usage into one dashboard, with usage history, upcoming quota refreshes, expiring earned resets, and configurable alerts.

Monitor personal and work accounts side by side, spot the next available allowance, and leave Scrooge watching from the system tray while you work.

[Features](#what-scrooge-does) · [Providers](#supported-providers) · [Getting Started](#getting-started) · [Build From Source](#build-and-run-from-source) · [Preview Status](#preview-status)

![AI Token Scrooge dashboard with usage history and upcoming quota events](docs/images/dashboard.png)

*Screenshots show the Windows development preview with synthetic, explicitly labeled demo data. They illustrate the interface, not live provider availability or subscription entitlements. Some preview UI may be ahead of published builds.*

## What Scrooge Does

- **One dashboard for multiple accounts.** Separate personal and work connections, provider icons, nicknames, and reported plan badges keep accounts recognizable.
- **Usage at a glance.** Each reported quota window gets its own used/remaining percentage bar, alert markers, and reset countdown. Session, weekly, and monthly limits stay separate.
- **A timeline of what happens next.** Reset Horizon puts upcoming quota refreshes and earned-reset expirations on a calendar-day timeline.
- **History alongside the future.** See recorded usage trends, then scroll toward upcoming events using the timeline navigator.
- **Alerts you control.** Choose thresholds, per-row alarms, exhaustion/restoration alerts, reminder schedules, notification sounds, quiet hours, and snooze.
- **Background monitoring.** Close to the system tray, optionally start with Windows, and return to the dashboard from the tray icon.
- **Read-only provider monitoring.** Scrooge reads usage; it does not generate AI responses, spend reset credits, buy credits, or switch accounts in your other apps.

## Supported Providers

| Provider | What Appears in Scrooge | Connection and Limits |
| --- | --- | --- |
| **OpenAI Codex** | Provider-reported usage windows, remaining capacity, reset times, and available earned resets when supplied | Browser sign-in through a local Codex installation, using separate Scrooge-owned profiles. General ChatGPT model quotas and OpenAI API billing are outside this integration. |
| **GitHub Copilot** | Monthly AI-credit/request usage, allowance when available, remaining percentage, and monthly refresh | Browser/device sign-in for personal billing; an advanced token path supports personal or organization billing. Billing data can lag. Automatic entitlement lookup uses an internal endpoint that may change; manual allowance is a labeled fallback. |
| **Claude** | Session and weekly usage windows reported for connected Pro/Max accounts | Browser sign-in through Claude Code **2.1.310 or later**, with isolated profiles. Monitoring does not send AI prompts. |

Only limits actually supplied by a provider are displayed. Unknown allowances stay unknown; a plan name alone is not used to invent a quota or subscription price. See [provider evidence](docs/provider-capabilities.md) and [verification notes](docs/verification.md) for the scope of live checks.

## Reading the Dashboard

![Multiple OpenAI, GitHub Copilot, and Claude accounts with separate usage windows](docs/images/accounts.png)

Each account groups its reported limits together. A short session limit can refill before a weekly limit, so each row has its own countdown and alert controls.

| Visual | Meaning |
| --- | --- |
| **Used / Left percentages** | How much of that particular allowance has been consumed and remains |
| **Green, blue, yellow, and red bar states** | Normal capacity, informational threshold, warning, and critical capacity |
| **Colored threshold handles** | Info, warning, and critical levels based on **percentage remaining**; drag them to adjust that row |
| **Bell controls** | Enable or mute alerts for the individual quota row |
| **Countdown and date** | The next reported refresh time for that quota window |
| **Account gear** | Rename, adjust polling, choose a history-line color, pause monitoring, manage alerts, reconnect, or remove the connection |

Click a usage meter for more detail. Sort accounts by **Most Remaining**, **Least Remaining**, or **Next Reset**, or use **Custom Order** and drag account handles into the order you prefer. Status messages distinguish stale readings, connection problems, and unknown data from healthy usage.

### Reset Horizon and Usage History

**Purple events** represent quota refreshes: an allowance is expected to become available again. **Amber events** represent earned-reset expiration: an unused reset credit is about to expire. These are different events—expiration does not refill your quota.

- Provider icons and plan badges identify each event. Hover or click for details.
- The checkable account icons on the left filter the timeline's usage lines and events.
- The **History** toggle shows or hides historical usage content. With history off, navigation begins at Today.
- The navigator at the bottom moves through the date range, including historical usage when enabled.
- Account line colors help distinguish overlapping history series. Account settings also supports adding reusable custom-color swatches through a color chooser in the current development preview.

History and alert records are retained for **45 days by default**. Repeated identical readings are compacted, while changes and reset events remain available. Read the [history storage guide](docs/history-storage.md) for details.

## Notifications and Settings

![Settings for time zone, popup notifications, icon alerts, sounds, and volume](docs/images/settings.png)

Settings saves changes automatically. Global settings establish defaults; account and quota-row controls let you tailor individual connections.

| Control | What It Does |
| --- | --- |
| **Popup Notifications / Alert on Icon** | Choose Windows popup delivery and app-icon alert status |
| **Default Alert Levels** | Set informational, warning, and critical percentages of capacity remaining |
| **Capacity Exhausted** | Alert when a limit reaches no remaining capacity, with its dashboard alarm enabled |
| **Capacity Restored** | Alert after an exhausted limit has capacity again and the provider confirms usage is allowed |
| **Earned Reset Expiration Alerts** | Configure lead times and severities before unused earned resets expire |
| **Quota Refresh Alerts** | Configure a separate schedule before quota refreshes |
| **Quiet Hours** | Quiet notification delivery during a chosen time range, including overnight ranges, while monitoring continues |
| **Sounds** | Choose Duck Sounds or Classic Chimes, adjust volume, and preview notification actions |
| **Alert History / Snooze** | Review recorded alerts and postpone eligible notifications |
| **Time Zone** | Control displayed dates, reset times, and quiet-hour interpretation |

Account settings provides a **1–30 minute polling interval**, with a default of **2 minutes**. Shorter intervals check more often; longer intervals make fewer requests. Known reset boundaries may bring a check forward, while retry waits and provider backoff take priority. **Monitor This Account** pauses checks and alerts without deleting the saved connection.

Windows notification settings and Do Not Disturb can affect popup delivery. Run the app normally, not as administrator. Use **Send Test Notification** to check your environment.

## Getting Started

1. Use a Windows preview package if one is available in [Releases](https://github.com/lancelarsen/AI-Token-Scrooge/releases), or build from source below.
2. Extract the **entire** portable package to a stable folder and run `AIResetRadar.App.exe`.
3. Select **Add account** and choose a provider. Complete the provider's sign-in in your browser.
4. Review the detected usage windows, then choose your alert thresholds and notification preferences.
5. Leave Scrooge running in the tray if you want background monitoring. Use **Exit Scrooge** in the tray menu to stop it.

The first launch has no connected accounts. OpenAI requires a local Codex installation; Claude requires the supported Claude Code version. GitHub browser sign-in requires the project's app registration to be configured—see [GitHub sign-in setup](docs/github-sign-in.md). The GitHub authorization screen currently uses the registered name **AI Reset Radar**.

Want to explore without connecting accounts? Launch with `--demo`. This uses labeled synthetic data and keeps demo changes in memory; the explicit notification-test action still uses Windows.

## Preview Status

This is an **active Windows desktop preview**, distributed as an unsigned portable build rather than a finished signed installer. The app is implemented, but complete release acceptance is still open.

Live provider coverage, independent-account renewal/removal checks, Windows notification activation, tray/startup recovery, DPI/accessibility, clean-user installation, and longer resource testing remain areas for further verification. A native CPU regression is tracked in [performance notes](docs/native-performance.md). Demo screenshots do not establish that any provider integration has passed live acceptance.

See [release acceptance](docs/release-acceptance.md), [verification](docs/verification.md), and the [implementation checklist](tasks/todo.md) for detailed evidence and outstanding work. Report reproducible problems through [GitHub Issues](https://github.com/lancelarsen/AI-Token-Scrooge/issues), including your app build, provider, steps, and a redacted screenshot—never include tokens or credentials.

## Build and Run From Source

Requires Windows and the pinned .NET 10 SDK with the `maui-windows` workload for building. The self-contained publish output is intended to run without a separately installed .NET runtime.

```powershell
dotnet build src/AIResetRadar.App/AIResetRadar.App.csproj
dotnet test AIResetRadar.slnx
dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish/win-x64
```

Run the published `artifacts/publish/win-x64/AIResetRadar.App.exe`. For a design preview, use `--demo`; `--compact` starts an 800Ã—700 logical-pixel window. The main window also fits within the current monitor's working area. Test data can be isolated with `--data-dir <absolute-folder>`.

Preview-only error cases use `--demo --scenario unknown`, `offline`, `auth`, `permission`, `exhausted`, `due`, `partial` or `reserve-only`. Demo account/settings changes, alert history and snooze changes stay in memory. The explicit notification test still asks Windows to display a test popup.

`./scripts/Build-Windows.ps1 -Package` tests, publishes and creates an unsigned preview ZIP with a SHA-256 manifest under `artifacts/releases/`. Use `-PublishDirectory artifacts/publish/validation-win-x64` to keep a running older review copy untouched. Extract the complete ZIP before launching the executable. The ZIP is a portable review package; clean-user acceptance and installer/signing remain open. The build requires the exact SDK in `global.json`; see `docs/toolchain.md` for the supported runtime pin.

The app initially opens without accounts. **Add account** offers OpenAI Codex, GitHub Copilot, or Claude. OpenAI sign-in happens in your own browser. GitHub's default **Sign in with GitHub** shows a short code and opens GitHub for approval. The developer must configure the one-time app registration described in `docs/github-sign-in.md`; normal users do not create tokens. Credentials are protected with Windows DPAPI and renewed when needed. **Use an access token instead** retains advanced personal/organization connections through a masked field. Personal billing requires Plan read permission; organization-funded usage needs authorized organization billing access. Removing a connection deletes its local credentials; GitHub App authorization can also be revoked in GitHub settings. GitHub usage may lag, and a usage report alone does not establish your allowance. General ChatGPT model quotas and OpenAI API billing are separate products and are not included.

Clicking the tray icon restores the main app. Its menu opens Scrooge, refreshes usage or exits. Closing the main window keeps the monitor running when close-to-tray is enabled. **Exit Scrooge** ends monitoring. Startup at Windows sign-in is enabled by default and can be disabled in Settings. Windows may place tray icons in its overflow menu. Native popup appearance and delivery depend on Windows settings and Do Not Disturb; alert history remains available in Scrooge.

Each account has a gear button for its name, polling interval, monitoring, reconnect and other alert rules. Reported **Available Resets** and expiration details appear inline. This is informational; Scrooge does not spend reset credits. Usage alert thresholds are adjusted directly on the dashboard bars; global Settings supplies defaults.

Settings opens over the dashboard and saves changes automatically. Quiet hours use two draggable endpoints in whole-hour steps and can cross midnight. Monitoring continues while notifications are quiet. Sounds can be previewed, switched between Duck Sounds and Classic Chimes, and adjusted with the volume control. Reset-expiration and quota-refresh reminders have separate editable schedules. A successful snooze clears the matching in-app banner.

## Local Data and Credentials

Usage history uses compact intervals for repeated readings, shared metadata, and incremental graph loading. Changes and reset events remain available, and drawing still preserves peaks at screen resolution. See [history storage](docs/history-storage.md) for format, migration backup and verification details.

Each account's settings include an auto-saved **Polling Interval** slider, from 1 to 30 minutes in whole-minute steps, defaulting to 2 minutes for every provider. Known quota reset/credit expiration boundaries can trigger an earlier refresh; provider retry waits and error backoff take priority. Reconnecting preserves the selected interval.

For reliable Windows notification activation and launch-at-sign-in, extract the preview into a stable folder before running it. Startup registration points to that executable path; moving or deleting it can break startup. After moving the app, launch it from the new location and toggle the launch-at-sign-in setting off and on if enabled. Run normally, not as administrator: Windows App SDK notifications are unsupported in elevated apps. See [Microsoft's notification guidance](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart).

Default application state: `%LOCALAPPDATA%\AIResetRadar`. SQLite stores non-secret connection metadata and usage/alert data. Protected token files are in `Credentials`; independent Codex profiles are in `Profiles`; Claude Code manages its own credentials in isolated `ClaudeProfiles` directories. Existing Codex/IDE profiles are not modified. Removing a connection deletes/signs out Scrooge's own saved credentials only. Logs must never include tokens or provider response bodies.

Reconnect validates a separate protected credential/profile before adopting it. It keeps the account's paused state and logical identity. Superseded credential references are queued durably for cleanup; a pending cleanup is shown and retried while monitoring. Removing a connection commits that removal before retiring its credentials/history. Snapshot and alert history retain 45 days by default.

Scrooge does not make inference requests, redeem resets, purchase credits or switch accounts in other applications. The Codex transport has an explicit method allowlist.

## Project Documentation

- `design/selection.md`: final visual selection and required details.
- `tasks/plan.md`: complete requirements and verification gates.
- `tasks/todo.md`: implementation checklist.
- `docs/toolchain.md`: verified environment.
- `docs/verification.md`: current evidence and remaining checks.

## Assets

The selected Token Baron logo (concept 01) was prepared with the built-in ImageGen tool as a transparent production asset at `src/AIResetRadar.App/Resources/Images/token_baron.png`. Its Windows ICO contains sizes from 16 to 256 pixels and is used for the executable, window/taskbar, and normal monitoring tray icon. The original radar asset is retained as historical design material. OpenAI's source icon was reused from the user's CreatorMicroModelKeys project. GitHub's icon comes from its official `github.githubassets.com/favicons/favicon-dark.svg` asset. Provider marks identify the services; Radar is an independent application.

Refresh/settings toolbar assets use the official Lucide library. Its license is included in `src/AIResetRadar.App/Resources/LUCIDE-LICENSE.txt` and the published output.
