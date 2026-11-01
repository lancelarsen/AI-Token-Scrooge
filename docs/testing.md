# Testing and Verification

Match verification to the behavior changed. Run focused tests while developing, then the relevant full suite before submitting. Documentation-only changes need link, Markdown, capitalization, and factual checks; they do not require an app restart.

## Commands

Run from the repository root on Windows with the pinned SDK and MAUI workload installed:

```powershell
dotnet test AIResetRadar.slnx --nologo
dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish/review-win-x64 --nologo
```

For a portable ZIP with the package checks:

```powershell
./scripts/Build-Windows.ps1 -Package -PublishDirectory artifacts/publish/review-win-x64
```

Launch the published executable with `--demo --data-dir <absolute-test-folder>`. Add `--compact` for the 800-by-700 preview. Stop the old instance before judging the new output; rebuilding does not replace an already-running process. Use the normal account flow only when you have an authorized test account.

## Test Boundaries

Core tests cover arithmetic, time, rules, scheduling, and identity policies. Infrastructure tests cover provider transport/mapping, persistence, and selected App presentation/orchestration files compiled into the test project. They do not launch the complete MAUI UI. A passing suite does not demonstrate keyboard accessibility, WinUI layout, or notification delivery.

Use injected TimeProvider/fake clocks, synthetic accounts, isolated temporary databases, and HTTP/CLI fixtures. Avoid sleeps and local-time assumptions. Test the configured zone explicitly, including DST. Do not clear shared resources belonging to another parallel test; fixture cleanup must stay inside that fixture's directory and connection pool.

## Change-Specific Evidence

| Change | Required Evidence |
| --- | --- |
| Quota Mapping | Unknown denominator, overage, missing windows/timestamps, provider/unit/scope identity |
| Provider Transport | Auth/permission errors, retry deadlines, cancellation, bounded reads, malformed responses, no inference fallback |
| Account Lifecycle | Reconnect, removal/disable during polling, rejected identity, persistence failure, late completion |
| Preferences | Old settings deserialize, save/reopen, concurrent updates preserve unrelated fields, failed save is reported |
| Retention or Storage | Boundary inclusion, restart, custom retention, account isolation, migration, rollback/failure, cleanup |
| Timeline | Shared anchors, history toggle, filtering, first/last day, scroll/resize, DST, real timestamp placement |
| UI | Freshly rebuilt app, desktop and compact sizes, long content, focus/keyboard, empty/error states |
| Windows Lifecycle | Close-to-tray, exit, notification click, single instance, startup and tray recovery |
| Packaging | Self-contained runtime, ZIP integrity, manifest, absence of user state/secrets, clean-user launch |

## Native Visual Review

Follow [Design Guidelines](design.md). Use the closest production view as the reference and report it. Exercise the actual control: dragging a snapped slider is different from displaying a rounded label. Confirm selectors, keyboard interaction, and all four capacity colors. Check that repeated polling does not destroy focus or an in-progress drag.

Test with Windows display scaling as well as a smaller window. Screenshots alone cannot establish keyboard or screen-reader support. If automation targets the wrong window, stop input and report the verification gap rather than claiming the visible check passed.

## Live Provider Evidence

Record the tested version, product, account type, billing/workspace scope, provider-screen comparison, and verification date in a sanitized note. Identify untested renewal, second-account, or organization scenarios explicitly. Never publish personal identifiers or credentials. See [Provider Integration](integration.md#required-verification).

## Reporting Results

Include commands and outcomes, the commit/build tested, and any remaining gaps. Separate fixture tests, native demo observations, and live-provider verification. Keep historical evidence in [Verification Notes](verification.md); do not turn dated observations into permanent guarantees.
