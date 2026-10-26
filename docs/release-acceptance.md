# Windows preview acceptance — October 26, 2026

The goal remains incomplete. This is a current index of release evidence and open gates; `tasks/plan.md` and `tasks/todo.md` retain the full requirements.

## Current preview

- Running build: `artifacts/publish/token-baron-win-x64`.
- Package: `artifacts/releases/AITokenScrooge-0.1.0-win-x64-preview.zip`.
- SHA256: `8CC234A25650BEC4236F5B15A5295A7A192E4C4F27480C515C43A7FF8A17DE80`.
- Self-contained Windows x64; unsigned preview.
- Complete solution test run: 147 passed, zero failed or skipped (59 Core, 88 Infrastructure). Release publish passed.

## Evidence and remaining gates

| Area | Evidence available | Still required |
|---|---|---|
| Selected UI | Native desktop and compact-window checks; grouped account bars, integrated daily timeline, themed dialogs/settings/buttons; long history text wraps | Full error-state keyboard/Narrator pass and 100/150/200% DPI checks |
| Accounts | OpenAI Plus, OpenAI Pro, and GitHub Max present after live refresh/restart; account-scoped settings and details | Complete live renewal, reconnect/removal isolation, external IDE preservation, and provider-source reconciliation matrix |
| Background lifetime | Duplicate launch exits 0; same process/window restored; native Close hides window; newer usage observation persisted while hidden | Tray-menu Exit, Explorer restart, sign-in startup, sleep/network recovery |
| Alerts | Evaluator, durable deduplication, SQLite failure/retry, and snooze tests; native in-app banner/history checks | Actual Windows View/Snooze activation while running/hidden/closed, notification-center inspection, disabled-notification behavior |
| Performance | Settled 120-second sample remained responsive but used 18.34 CPU seconds, about 15.3% of one core | Native CPU attribution and a meaningful post-fix comparison; longer resource acceptance |
| Distribution | Self-contained publish and ZIP contents/hash verified | Clean-user launch, notification activation, cleanup/uninstall acceptance; release distribution/signing decision |

## Current access limitations

The Computer Use app inventory returned no Windows shell or notification surface. Native tray-menu and notification-center interaction is therefore not proven by the process and in-app checks above.

Windows Performance Recorder is installed and exposes its CPU profile. The current session is not elevated. `scripts/Record-NativeCpu.ps1` is prepared and syntax-checked for a 60-second local, system-wide CPU trace. User approval for its administrator prompt and collection scope is pending; the script has not been executed.

Do not repeat counter samples as a substitute for CPU attribution, or treat automated alert tests as proof of OS notification activation. Detailed historical evidence is in `verification.md` and `native-performance.md`.
