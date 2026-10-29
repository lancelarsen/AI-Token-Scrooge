# Unified Reset Horizon History: Task Cards

Contract: [history-plan.md](history-plan.md). Implementation is present; checked items have current source/test evidence. Remaining unchecked items retain their full acceptance scope. Preserve the existing app and account data. Proposed new file names are guidance, not a requirement to create unnecessary abstractions.

## H1. Read a Complete Historical Interval

Provide a chronological date-bounded query without the current implicit 10,000-record truncation.

- [x] Query the requested account/range plus an optional preceding sample; preserve 30-day retention and account-removal cleanup.
- [x] Handle empty ranges, duplicate timestamps, cancellation, and malformed/unusable records without confusing them with zero usage.

Verification: focused store tests for boundaries, chronological order, account isolation, and persistence across reopen. Dependencies: none. Likely files: `LocalStore.cs` or a partial history file; `LocalStoreTests.cs`. Scope: S.

## H2. Capture the First Accepted Snapshot

Include a new/reconnected account's first accepted snapshot in history, matching the monitoring path's identity checks and error handling.

- [x] Accepted connection state and its first history record commit atomically; failed/rejected connections leave no historical sample. A database write failure retains the prior persisted state and surfaces an error. Subsequent successful polls keep recording normally.
- [x] Reconnects do not join different identities, turn stale data into fresh samples, or modify other application sign-ins.

Verification: focused accepted-connection/history tests and build. Dependencies: H1. Likely files: connection action file, `MainPage.Actions.cs`, targeted tests. Scope: M.

## H3. Project Weekly and Monthly History

Build pure account series and evidence-labeled historical events from saved snapshots.

- [x] OpenAI/Claude Weekly and Copilot Monthly produce one series per account; short windows are excluded only from history. Missing/ambiguous eligible windows are explicit and never averaged.
- [x] Missing and offline observations interpolate between compatible known samples and are identified as estimates; identity/allowance changes remain breaks. Measured replenishment preserves actual values and timestamps.
- [x] Historical refresh/expiry schedules are deduplicated and distinguished from observed replenishment; exclude short-window historical refreshes, with no inferred reset redemption or artificial refill. Future horizon events remain unchanged.

Verification: projection fixtures covering duplicate providers, renamed accounts, revised schedules, 60% to 91% replenishment, expiry without refill, unknown percentages, and downsampling that preserves transitions. Dependencies: H1. Likely files: new history projection model, model tests, provider mapping fixtures if necessary. Scope: M.

## H4. Model One Past/Future Viewport

Define date-to-position mapping and Today anchoring before integrating chart drawing.

- [x] One scroll range covers seven days behind Today through the furthest future event; initial position is Today, and navigator movement is continuous.
- [x] Calendar days have equal width across DST/time-zone changes; minimum readable width, resize, and range clamping behave consistently.
- [x] Distinguish following Today from browsing fixed dates so refreshes do not snap the viewport back.

Verification: pure viewport tests for initial anchor, left/right bounds, seven-day history, more than 30 future days, DST, resize, and refresh preservation. Dependencies: none; agree on the H3 event/series contract. Likely files: new viewport model and tests. Scope: S.

## Checkpoint: Data and Navigation

- [ ] H1-H4 focused tests pass and the current Windows app still builds
- [ ] No provider/account behavior has been changed beyond accepted-snapshot recording
- [ ] The graph contract treats scheduled times and measured values separately

## H5. Draw the Selected History View

Implement the shared chart using existing MAUI graphics and popup patterns.

- [ ] The graph shows percent used, stable per-account colors, restrained fills, actual historical event icons, and legible endpoint labels; no future line projection.
- [ ] Hover/click/focus reveals actual sample/event details without delay; estimated spans and partial history are identified.
- [ ] Renderer clips and downsamples appropriately without smoothing away replenishment or creating false precision.

Verification: projection-to-render fixtures where useful, then native screenshots at desktop/narrow widths and 100%/125%/150% display scaling where available. Compare with the selected reference and existing horizon. Dependencies: H3, H4. Likely files: new history graphics control/drawable, `HorizonView.cs`, focused projection tests. Scope: M.

## H6. Add Account Visibility Controls

Add the History toggle and compact provider selector rail, with one clickable icon per eligible account.

- [ ] Weekly/monthly account icons use nickname/plan identity; no quota-window sub-selectors. Multiple providers/accounts can be selected together; deselection hides the line.
- [ ] Selection auto-saves across restart and cannot disable monitoring or alerts; removed accounts are cleaned up and newly eligible accounts start visible.
- [ ] The History toggle starts at Today, reuses the navigator, and returns to the existing future-only behavior when off.

Verification: persistence/defaults tests plus keyboard and native click checks for duplicate-provider accounts. Dependencies: H5. Likely files: horizon/selector control, settings or dedicated view preferences, preference persistence, focused tests. Scope: M.

## H7. Preserve the View During Live Updates

Integrate asynchronous loading with the dashboard without recreating or flashing the horizon.

- [ ] Refresh/update history data in place, cancel obsolete loads, and preserve fixed-date or Today-following state as appropriate.
- [ ] Meaningful new history samples update the graph even when the existing dashboard render signature would otherwise remain unchanged.
- [ ] Account ordering, modal dialogs, alert-threshold drags, selector changes, and ordinary polling do not reset history scroll or interrupt native input.

Verification: focused state tests and repeated native pan/refresh/reorder/resize cycles, including partial history and an empty account list. Dependencies: H2, H6. Likely files: `MainPage.cs`, history-loading partial, `HorizonView.cs`, render/view state, focused tests. Scope: M.

## H8. Verify and Ship the Running App

- [ ] Run relevant automated tests and Windows build, then restart the latest build using the existing data/profile path. Confirm signed-in accounts remain available.
- [ ] Compare the running app with the selected reference at desktop and narrow widths; verify the unified navigator, seven days of history, 30+ future days, selection persistence, immediate details, no invented history, and no scroll jump on refresh.
- [ ] Update verification evidence, commit/push the completed increment, and report any unverified native/DPI or historical-data coverage limits precisely.

Verification: repository build/test scripts and live Windows app checks. Dependencies: H7. Likely files: `docs/verification.md`, these task cards, scoped fixes only. Scope: S.

Commit/push each completed increment. The standing rebuild/restart/visible-verification rule applies to H2 and every UI/runtime increment, not just H8. Do not mark a task complete solely because its build passed.
