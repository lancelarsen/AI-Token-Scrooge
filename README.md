<p align="center">

  <img src="src/AIResetRadar.App/Resources/Images/token_baron.png" alt="AI Token Scrooge mascot" width="120">

</p>

# AI Token Scrooge

**Never Waste a Token! Never Miss a Reset!**

AI Token Scrooge keeps your OpenAI Codex, GitHub Copilot, and Claude usage in one Windows app. See how much you have left, when each limit resets, and whether you have earned resets that are about to expire.

If you use several AI accounts, checking each provider's usage page gets old fast. Scrooge puts those accounts side by side and can alert you before you run out or miss a reset. Close the window and it can keep monitoring from the system tray while you work.

[Features](#what-scrooge-does) · [Providers](#supported-providers) · [Getting started](#getting-started) · [Build from source](#build-and-run-from-source) · [Preview status](#preview-status)

![AI Token Scrooge dashboard with usage history and upcoming quota events](docs/images/dashboard.png)

## What Scrooge does

- Plan your next session with Reset Horizon. See when your AI limits refill and which earned resets are about to expire, with your usage history on the same timeline.
- Know how much you have left before you start. Clear usage bars and countdowns show what's available across Codex, Copilot, and Claude, including separate session and weekly limits when reported.
- Get a heads-up before you run out, and an alert when capacity is back. Set reminders for upcoming refreshes and expiring resets so you can put your available usage to work.
- Make alerts work on your schedule. Choose sounds, set quiet hours, snooze reminders, or mute individual limits.
- Keep working while Scrooge keeps watch. It can start with Windows and monitor from the system tray, with the dashboard one click away.

Scrooge only monitors usage. It doesn't send AI prompts, redeem earned resets, purchase credits, or change the account you're using in another app.

## Supported providers

| Provider | What Scrooge shows | Connection and limits |
| --- | --- | --- |
| OpenAI Codex | Provider-reported usage windows, remaining capacity, reset times, and available earned resets when supplied | Browser sign-in through a local Codex installation, using separate Scrooge-owned profiles. General ChatGPT model quotas and OpenAI API billing are outside this integration. |
| GitHub Copilot | Monthly AI-credit/request usage, allowance when available, remaining percentage, and monthly refresh | Browser/device sign-in for personal billing; an advanced token path supports personal or organization billing. Billing data can lag. Automatic entitlement lookup uses an internal endpoint that may change; manual allowance is a labeled fallback. |
| Claude | Session and weekly usage windows reported for connected Pro/Max accounts | Browser sign-in through Claude Code 2.1.310 or later, with isolated profiles. Monitoring does not send AI prompts. |

Scrooge displays the limits a provider supplies. If an allowance is unknown, it says so. A plan name alone cannot tell us its quota or price. See [provider evidence](docs/provider-capabilities.md) and [verification notes](docs/verification.md) for the scope of live checks.

## Reading the dashboard

![Multiple OpenAI, GitHub Copilot, and Claude accounts with separate usage windows](docs/images/accounts.png)

An account can have more than one limit. Your session allowance might refill tonight while the weekly allowance stays nearly empty until Thursday. Each row has its own countdown and alert controls for that reason.

| Visual | Meaning |
| --- | --- |
| Used / Left percentages | How much of that particular allowance has been consumed and remains |
| Green, blue, yellow, and red bar states | Normal capacity, informational threshold, warning, and critical capacity |
| Colored threshold handles | Info, warning, and critical levels based on percentage remaining; drag them to adjust that row |
| Bell controls | Enable or mute alerts for the individual quota row |
| Countdown and date | The next reported refresh time for that quota window |
| Account gear | Rename, adjust polling, choose a history-line color, pause monitoring, manage alerts, reconnect, or remove the connection |

Click a usage bar for details. Sort by Most Remaining, Least Remaining, or Next Reset to find the account you need. For a fixed order, choose Custom Order and drag the account handles. Status messages tell you when a reading is stale, a connection needs attention, or usage is unknown.

### Reset Horizon and usage history

Purple events mark quota refreshes, when an allowance is expected to refill. Amber events mark earned-reset expirations, when an unused reset credit will expire. An expiring credit does not refill your quota.

- Provider icons and plan badges identify each event. Hover or click for details.

- The checkable account icons on the left filter the timeline's usage lines and events.

- The History toggle shows or hides historical usage content. With history off, navigation begins at Today.

- The navigator at the bottom moves through the date range, including historical usage when enabled.

- Give each account a different line color to make its history easier to follow. In the current development preview, the + swatch opens a color chooser. Added colors stay in the palette for reuse.

Scrooge keeps history and alerts for 45 days by default. It compacts repeated identical readings and retains changes and reset events. Read the [history storage guide](docs/history-storage.md) for details.

## Notifications and settings

![Settings for time zone, popup notifications, icon alerts, sounds, and volume](docs/images/settings.png)

Changes in Settings save automatically. Set your defaults there, then adjust individual accounts and quota rows where needed.

| Control | What it does |
| --- | --- |
| Popup Notifications / Alert on Icon | Choose Windows popup delivery and app-icon alert status |
| Default Alert Levels | Set informational, warning, and critical percentages of capacity remaining |
| Capacity Exhausted | Alert when a limit reaches no remaining capacity, with its dashboard alarm enabled |
| Capacity Restored | Alert after an exhausted limit has capacity again and the provider confirms usage is allowed |
| Earned Reset Expiration Alerts | Configure lead times and severities before unused earned resets expire |
| Quota Refresh Alerts | Configure a separate schedule before quota refreshes |
| Quiet Hours | Quiet notification delivery during a chosen time range, including overnight ranges, while monitoring continues |
| Sounds | Choose Duck Sounds or Classic Chimes, adjust volume, and preview notification actions |
| Alert History / Snooze | Review recorded alerts and postpone eligible notifications |
| Time Zone | Control displayed dates, reset times, and quiet-hour interpretation |

Choose how often to check each account with the Polling Interval slider: 1 to 30 minutes, starting at 2 minutes by default. A shorter interval catches changes sooner but makes more requests. Scrooge may check earlier at a known reset time; provider retry delays still take priority. Turn off Monitor This Account to pause its checks and alerts without losing the connection.

Windows notification settings and Do Not Disturb can affect popup delivery. Run the app normally, not as administrator. Use Send Test Notification to check your environment.

## Getting started

1. Use a Windows preview package if one is available in [Releases](https://github.com/lancelarsen/AI-Token-Scrooge/releases), or build from source below.

2. Extract the entire portable package to a stable folder and run `AIResetRadar.App.exe`.

3. Select Add account and choose a provider. Complete the provider's sign-in in your browser.

4. Review the detected usage windows, then choose your alert thresholds and notification preferences.

5. Leave Scrooge running in the tray if you want background monitoring. Use Exit Scrooge in the tray menu to stop it.

The first launch has no connected accounts. OpenAI requires a local Codex installation; Claude requires the supported Claude Code version. GitHub browser sign-in requires the project's app registration to be configured. See [GitHub sign-in setup](docs/github-sign-in.md). The GitHub authorization screen currently uses the registered name AI Reset Radar.

To try the interface before connecting an account, launch with `--demo`. Demo data is labeled, and changes stay in memory. Send Test Notification still displays a real Windows test notification.

## Preview status

This is a working Windows preview. The portable build is unsigned, and there is no finished signed installer yet.

We still need broader live-provider testing, including renewal and removal across independent accounts. Windows notification activation, tray and startup recovery, DPI and accessibility, installation for a new Windows user, and longer resource tests also need more work. The [performance notes](docs/native-performance.md) track a native CPU regression. Demo screenshots don't verify a live integration.

See [release acceptance](docs/release-acceptance.md), [verification](docs/verification.md), and the [implementation checklist](tasks/todo.md) for detailed evidence and outstanding work. Report reproducible problems through [GitHub Issues](https://github.com/lancelarsen/AI-Token-Scrooge/issues), including your app build, provider, steps, and a redacted screenshot. Never include tokens or credentials.

## Build and run from source

Build on Windows with the exact .NET 10 SDK pinned in `global.json` and the `maui-windows` workload. The self-contained publish output is intended to run without a separate .NET runtime installation.

```powershell

dotnet build src/AIResetRadar.App/AIResetRadar.App.csproj

dotnet test AIResetRadar.slnx

dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish/win-x64

```

Run the published `artifacts/publish/win-x64/AIResetRadar.App.exe`. For a design preview, use `--demo`; `--compact` starts an 800Ã. 700 logical-pixel window. The main window also fits within the current monitor's working area. Test data can be isolated with `--data-dir <absolute-folder>`.

Preview-only error cases use `--demo --scenario unknown`, `offline`, `auth`, `permission`, `exhausted`, `due`, `partial` or `reserve-only`. Demo account/settings changes, alert history and snooze changes stay in memory. The explicit notification test still asks Windows to display a test popup.

`./scripts/Build-Windows.ps1 -Package` tests, publishes and creates an unsigned preview ZIP with a SHA-256 manifest under `artifacts/releases/`. Use `-PublishDirectory artifacts/publish/validation-win-x64` to keep a running older review copy untouched. Extract the complete ZIP before launching the executable. The ZIP is a portable review package; clean-user acceptance and installer/signing remain open. The build requires the exact SDK in `global.json`; see `docs/toolchain.md` for the supported runtime pin.

The app initially opens without accounts. Add account offers OpenAI Codex, GitHub Copilot, or Claude. OpenAI sign-in happens in your own browser. GitHub's default Sign in with GitHub shows a short code and opens GitHub for approval. The developer must configure the one-time app registration described in `docs/github-sign-in.md`; normal users do not create tokens. Credentials are protected with Windows DPAPI and renewed when needed. Use an access token instead retains advanced personal/organization connections through a masked field. Personal billing requires Plan read permission; organization-funded usage needs authorized organization billing access. Removing a connection deletes its local credentials; GitHub App authorization can also be revoked in GitHub settings. GitHub usage may lag, and a usage report alone does not establish your allowance. General ChatGPT model quotas and OpenAI API billing are separate products and are not included.

Clicking the tray icon restores the main app. Its menu opens Scrooge, refreshes usage or exits. Closing the main window keeps the monitor running when close-to-tray is enabled. Exit Scrooge ends monitoring. Startup at Windows sign-in is enabled by default and can be disabled in Settings. Windows may place tray icons in its overflow menu. Native popup appearance and delivery depend on Windows settings and Do Not Disturb; alert history remains available in Scrooge.

Each account has a gear button for its name, polling interval, monitoring, reconnect and other alert rules. Reported Available Resets and expiration details appear inline. This is informational; Scrooge does not spend reset credits. Usage alert thresholds are adjusted directly on the dashboard bars; global Settings supplies defaults.

Settings opens over the dashboard and saves changes automatically. Quiet hours use two draggable endpoints in whole-hour steps and can cross midnight. Monitoring continues while notifications are quiet. Sounds can be previewed, switched between Duck Sounds and Classic Chimes, and adjusted with the volume control. Reset-expiration and quota-refresh reminders have separate editable schedules. A successful snooze clears the matching in-app banner.

## Local data and credentials

Usage history uses compact intervals for repeated readings, shared metadata, and incremental graph loading. Changes and reset events remain available, and drawing still preserves peaks at screen resolution. See [history storage](docs/history-storage.md) for format, migration backup and verification details.

Each account's settings include an auto-saved Polling Interval slider, from 1 to 30 minutes in whole-minute steps, defaulting to 2 minutes for every provider. Known quota reset/credit expiration boundaries can trigger an earlier refresh; provider retry waits and error backoff take priority. Reconnecting preserves the selected interval.

For reliable Windows notification activation and launch-at-sign-in, extract the preview into a stable folder before running it. Startup registration points to that executable path; moving or deleting it can break startup. After moving the app, launch it from the new location and toggle the launch-at-sign-in setting off and on if enabled. Run normally, not as administrator: Windows App SDK notifications are unsupported in elevated apps. See [Microsoft's notification guidance](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart).

Default application state: `%LOCALAPPDATA%\AIResetRadar`. SQLite stores non-secret connection metadata and usage/alert data. Protected token files are in `Credentials`; independent Codex profiles are in `Profiles`; Claude Code manages its own credentials in isolated `ClaudeProfiles` directories. Existing Codex/IDE profiles are not modified. Removing a connection deletes/signs out Scrooge's own saved credentials only. Logs must never include tokens or provider response bodies.

Reconnect validates a separate protected credential/profile before adopting it. It keeps the account's paused state and logical identity. Superseded credential references are queued durably for cleanup; a pending cleanup is shown and retried while monitoring. Removing a connection commits that removal before retiring its credentials/history. Snapshot and alert history retain 45 days by default.

Scrooge does not make inference requests, redeem resets, purchase credits or switch accounts in other applications. The Codex transport has an explicit method allowlist.

## Project documentation

- `design/selection.md`: final visual selection and required details.

- `tasks/plan.md`: complete requirements and verification gates.

- `tasks/todo.md`: implementation checklist.

- `docs/toolchain.md`: verified environment.

- `docs/verification.md`: current evidence and remaining checks.

## Assets

The selected Token Baron logo (concept 01) was prepared with the built-in ImageGen tool as a transparent production asset at `src/AIResetRadar.App/Resources/Images/token_baron.png`. Its Windows ICO contains sizes from 16 to 256 pixels and is used for the executable, window/taskbar, and normal monitoring tray icon. The original radar asset is retained as historical design material. OpenAI's source icon was reused from the user's CreatorMicroModelKeys project. GitHub's icon comes from its official `github.githubassets.com/favicons/favicon-dark.svg` asset. The provider icons identify their services. AI Token Scrooge is an independent application.

Refresh/settings toolbar assets use the official Lucide library. Its license is included in `src/AIResetRadar.App/Resources/LUCIDE-LICENSE.txt` and the published output.
