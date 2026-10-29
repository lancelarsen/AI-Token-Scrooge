# AI Reset Radar — implementation task cards

Current feature queue (October 29, 2026): [Unified Reset Horizon History Task Cards](history-todo.md). See [the feature plan](history-plan.md) for the selected visual, unified scrolling behavior, and Weekly/Monthly-only history scope.

Implementation is in progress. Checked items have concrete evidence; unmarked combined acceptance items can have implemented portions and still require live/native verification. Read `plan.md` and `../docs/verification.md`. Each task should leave a working increment; likely files are guidance, not a mandate to spread simple logic across files. Tests and evidence belong beside the task's actual behavior.

## Phase A — decisions and feasibility

### A1. Record the selected visual direction

Choose the user's image(s) from `design/concepts.md`, preserve any requested combination, and record how the main view, compact view and reset reserve should relate.

Acceptance:
- [x] `design/selection.md` identifies exact image paths and the user's choices; no default is presented as approved.
- [x] Tokens and selected layout reference Creator Micro; generated date/color/label inconsistencies are explicitly excluded from the implementation specification.

Verification: review the selected images and written brief together. Dependencies: none. Likely files: `design/selection.md`, `design/concepts.md`. Scope: S.

### A2. Verify Windows build prerequisites

Acceptance:
- [x] Supported SDK/MAUI/Windows SDK versions and Windows architecture are recorded, including any installer permissions needed.
- [ ] A disposable minimal MAUI build verifies the intended toolchain before choosing package versions; preserve project settings and user work.

Verification: SDK/workload inventory and minimal native build; do not claim workload health based only on an installed pack directory. Dependencies: none. Likely files: `docs/toolchain.md`, later `global.json` and package configuration. Scope: S.

### A3. Prove Codex read capabilities

Acceptance:
- [ ] Generate/inspect the installed protocol schema, record the executable version, and use only the necessary identity and rate-limit reads for an authorized test account.
- [ ] Redacted evidence maps multiple windows and earned reset count/detail/expiry, with unavailable capabilities explicitly recorded; no inference or redemption operation is called.

Verification: compare returned fields with provider-visible usage, exercise null/partial data fixtures and check process shutdown. Dependencies: A2. Likely files: `docs/provider-capabilities.md`, `tests/Fixtures/Codex/*`. Scope: M.

### A4. Prove independent Codex account storage

Acceptance:
- [ ] Two separately authenticated accounts remain correctly identified while polling and renewing credentials; sign-out/remove of one does not affect the other.
- [ ] Existing Codex/IDE account remains unchanged; app-owned keyring/profile namespace and refresh-token ownership are documented and verified.
- [ ] No plaintext fallback occurs silently; unsupported isolation is a clear blocker rather than a hidden shared login.

Verification: controlled sign-in/refresh/restart/disconnect matrix with before/after identity checks; record any missing second-account access. Dependencies: A3. Likely files: `docs/account-isolation.md`, spike-specific fixtures and auth tests. Scope: M.

### A5. Prove GitHub billing access and semantics

Acceptance:
- [ ] Confirm the token permission and personal versus organization billing scope; resolve actual authenticated identity before linking snapshots.
- [ ] Reconcile returned credit/request quantities, included allowance, paid overage and reporting latency with the provider UI/export; list any denominator that remains unknown.
- [ ] Record the supported account types without treating organization totals or activity metrics as personal remaining quota.

Verification: read-only request matrix and redacted fixtures, including permission denial. Dependencies: A2. Likely files: `docs/provider-capabilities.md`, `tests/Fixtures/GitHub/*`. Scope: M.

### A6. Prove Windows tray and notification lifecycle

Acceptance:
- [ ] A minimal native spike verifies tray add/update/restore, running taskbar overlay, app notification and click activation in the intended release package style.
- [ ] Cold activation routes into one app instance; package identity, startup and signing requirements are recorded.

Verification: actual Windows UI interaction, Explorer restart and one cold-start notification click. Dependencies: A2. Likely files: `docs/windows-integration.md`, minimal temporary spike files. Scope: M.

### Checkpoint A

- [ ] Visual choice is settled before UI work; account and Windows feasibility evidence is recorded.
- [ ] Real access blockers are visible. Independent work may continue, but blocked live checks stay unchecked.

## Phase B — complete demo experience

### B1. Establish the solution and dark design resources

Acceptance:
- [x] App/Core/Infrastructure and test projects build with supported pinned dependencies; domain code has no MAUI dependency.
- [ ] Shared resource dictionaries reproduce the selected Creator Micro typography, colors, controls and spacing; demo/live mode is explicit.

Verification: native launch, targeted project build, small visual comparison with source screenshots. Dependencies: A1, A2, A6. Likely files: solution/build configuration, `MauiProgram.cs`, `Resources/Styles/*`. Split setup/theme if more than five production files are needed. Scope: M.

### B2. Model quotas and time correctly

Acceptance:
- [x] Account/scope/window identity and capability-aware snapshots represent unknown values, period boundaries and allowance provenance.
- [x] Usage math preserves absolute overage, clamps only visual ranges and never combines incompatible units.
- [x] UTC timestamps, countdowns, reset-due state and DST-safe formatting use a testable clock.

Verification: deterministic tests for percent/absolute/unknown/overage, two windows, Unix seconds, month/year/DST changes and invalid inputs. Dependencies: B1. Likely files: Core account and snapshot records, quota calculator and time formatter. Scope: M.

### B3. Model earned reset reserves

Acceptance:
- [x] Count, detail availability, partial details, status and nullable expiry are independent fields.
- [x] Earliest known expiry ignores unusable entries; local expiry does not rewrite the last-reported authoritative count.

Verification: null count versus zero, null versus empty detail list, capped details, unknown expiry, repeated IDs, expired items and expiry boundary tests. Dependencies: B2. Likely files: Core reset records and reserve evaluator. Scope: S.

### B4. Deliver the demo dashboard and account detail

Acceptance:
- [ ] Selected layout shows multiple accounts, named quota windows, used/left/reset, exact time and freshness with explicitly labeled sample data.
- [ ] Account selection and Refresh work against a deterministic demo provider; unknown/loading/offline/auth states can be inspected.

Verification: real native interactions at 1440×1024 and 800×700; compare source and selected image. Dependencies: B1–B3. Likely files: dashboard/detail views and view models, demo provider. Scope: M.

### B5. Deliver the reserve view and shared UI states

Acceptance:
- [ ] User can see the owning account, count and known expiries without confusing earned resets with automatic quota resets.
- [ ] Empty, unavailable, partial, expiry-soon and expired-pending-refresh states have readable labels and keyboard access.

Verification: sample-state native screenshot matrix and Narrator/focus pass. Dependencies: B3, B4. Likely files: reserve view/view model, shared status component. Scope: M.

### Checkpoint B

- [ ] Native demo experience looks like the approved reference and reflows without clipping.
- [ ] Domain tests pass; all demo values are clearly marked and no live capability is implied.

## Phase C — live accounts

### C1. Persist account connections securely

Acceptance:
- [ ] Local account metadata and protected secret references round-trip; no token reaches SQLite, logs or settings export.
- [ ] Add/rename/disable/remove preserve other accounts and delete only app-owned credentials on disconnect.

Verification: restart, removal, read/write failure and secret-redaction tests using a fake credential store, plus Windows-store smoke test. Dependencies: A4, A5, B1. Likely files: account repository, credential-store abstraction/Windows implementation, connection service. Scope: M.

### C2. Connect a Codex account end to end

Acceptance:
- [ ] Supported sign-in and identity checks feed actual multi-bucket usage into the dashboard; unknown fields remain unknown.
- [ ] Adapter uses an app-owned minimal profile, explicit protocol allowlist, cancellable transport and bounded child lifetime.
- [ ] Reauthentication is actionable and cannot replace another account's data.

Verification: protocol fixture tests, interrupted process/identity mismatch cases and authorized live comparison. Dependencies: A3, A4, C1, B4. Likely files: Codex transport/client/mapper, connection view/view model. Scope: M; split authentication from transport if needed.

### C3. Connect Codex earned reset status

Acceptance:
- [ ] Real reserve count/details/expiry populate the reserve view when available, including count-only and partial-detail responses.
- [x] No consume/reset-credit endpoint is reachable from monitoring workflows.

Verification: adapter mapping tests and live read where the account has reserve access; absence is handled and reported. Dependencies: C2, B5. Likely files: Codex reserve mapper, reserve binding and tests. Scope: S.

### C4. Prove simultaneous account monitoring in the app

Acceptance:
- [ ] Two same-provider accounts survive add, refresh, renewal and app restart with correct subject/scope binding.
- [ ] Disabling/removing one leaves the other and existing external Codex/IDE login intact; duplicate scope detection is accurate.

Verification: end-to-end two-account matrix with timestamps and redacted account labels. Dependencies: C1–C3. Likely files: account coordinator and any scoped-auth corrections. Scope: M.

### C5. Connect Copilot usage end to end

Acceptance:
- [ ] Personal billing usage maps credits or legacy requests to the correct account/period, with documented API version and permissions.
- [ ] Only an established same-scope allowance produces remaining capacity; manual allowance is labeled; unknown usage/limit and paid overage are distinct.
- [ ] Organization-funded accounts show access limitations or verified owner scope, never fabricated personal quota.

Verification: quantities/units/discount fixtures, 401/403/429 tests and authorized live reconciliation; record reporting lag. Dependencies: A5, C1, B4. Likely files: GitHub client/mapper, allowance resolver, connection UI. Scope: M.

### Checkpoint C

- [ ] Live values agree with the provider source within documented reporting delay; account isolation is proven.
- [ ] No required live check is called complete using fixtures or demo data.

## Phase D — monitor and notify

### D1. Persist snapshots and coordinate refresh

Acceptance:
- [ ] SQLite stores bounded history/settings; restart restores clearly aged snapshots and migrations preserve account identity.
- [ ] Cancellable refreshes have no overlap per account, bounded concurrency, timeout/backoff/Retry-After and visible stale status.
- [ ] Reset/expiry boundaries trigger bounded confirmation reads; resume/network recovery reconcile missed events.

Verification: fake-clock/offline/resume/concurrency tests, DB migration test and measured process/network count. Dependencies: C2, C5. Likely files: snapshot store/migration, refresh coordinator, lifecycle hooks. Scope: M; split persistence and scheduling if needed.

### D2. Deliver tray quick view and desktop lifetime

Acceptance:
- [ ] Tray icon/flyout shows account-scoped usage and next events; its state changes only when relevant data/state changes.
- [ ] Close-to-tray, restore, Exit, optional launch-at-sign-in and single instance work; Explorer restart recreates the icon.
- [ ] Taskbar overlay reflects monitored state while open; tooltips explain limiting and stale sources without claiming a global percent.

Verification: actual Windows interaction at multiple DPIs, Explorer restart, hide/restore/startup/Exit, icon-handle cleanup. Dependencies: A6, B4, D1. Likely files: Windows tray/status/lifetime services and flyout view/view model. Scope: M; split flyout from lifecycle if needed.

### D3. Implement persistent alert decisions

Acceptance:
- [ ] Low thresholds, confirmed restoration and earned-credit expiry generate one event for the right account/window/period or credit.
- [ ] Quiet hours, snooze, restarts and multi-threshold jumps avoid duplicates; absent timestamps never create scheduled expiry alerts.

Verification: deterministic clock/state tests for transitions, start-low behavior, period rollover, DST quiet hours, stale values, snooze and restart persistence. Dependencies: B2, B3, D1. Likely files: rules, evaluator and event repository. Scope: M.

### D4. Deliver native notifications and alert history

Acceptance:
- [ ] Native notifications and in-app banners represent the same persisted event; View/Snooze/Dismiss work with exact-account routing.
- [ ] Cold launch routes into a single instance; disabled OS notifications and privacy mode work visibly.
- [ ] Alert settings, test notification and history are functional; any optional scheduled reminders are canceled/reconciled after source changes.

Verification: native activation while running/hidden/closed, snooze test, quiet-hours test, sleep/resume and notification-center inspection. Dependencies: A6, D2, D3. Likely files: Windows notification service/router, history/settings views and view models. Scope: M; split settings/history if needed.

### Checkpoint D

- [ ] The monitor remains useful with its main window hidden and recovers after resume/network failure.
- [ ] Alerts neither claim unconfirmed restoration nor repeat on each poll/restart.

## Phase E — polish and delivery

### E1. Complete native visual and accessibility QA

Acceptance:
- [ ] Compare live native screens against the chosen concept and Creator Micro at both logical viewport sizes and 100/150/200% scaling.
- [ ] All main flows and error states work with keyboard/Narrator, readable contrast and no color-only information; record and fix material visual gaps.

Verification: screenshot pairs plus actual interaction notes, not compilation alone. Dependencies: C4, D4. Likely files: selected views/styles and `docs/verification.md`. Scope: M per correction batch.

### E2. Verify resilience and resource behavior

Acceptance:
- [ ] Power/network/auth/protocol/DB failures preserve correct account boundaries and recover or show an actionable state.
- [ ] Idle memory, polling rate, child processes and shutdown are measured; no token disclosure, notification storms or unbounded resource growth appears.

Verification: focused fault scenarios, relevant test suites and native soak session with recorded duration/results. Dependencies: E1. Likely files: fixes in affected service, `docs/verification.md`. Scope: M per failure group.

### E3. Package and document the Windows release

Acceptance:
- [ ] Release build/tests and a reproducible self-contained Windows x64 package pass; clean-user install, launch, notification activation and uninstall are exercised.
- [ ] README covers connections, capability limits, freshness, tray/Exit, startup, notifications, troubleshooting and credentials removal; no secrets or demo-as-live claims.
- [ ] Final report identifies references, live provider/account coverage, package path, checks run and real outstanding gaps. Distribution/signing actions stay within approved scope.

Verification: `dotnet test` on the actual test projects, targeted Release MAUI build/publish and native package smoke test using the discovered RID/framework. Do not run mobile workloads. Dependencies: E2. Likely files: publish profile/manifest, README, verification report. Scope: M.

### Final checkpoint

- [ ] Required acceptance checks pass, including authorized live multi-account/provider coverage.
- [ ] Selected UI, background monitor, earned-reset expiry and notification workflows are reviewable in the running Windows app.
- [ ] Required gaps are resolved or explicitly remain open; do not mark a future goal complete merely because the demo runs or its budget is ending.
