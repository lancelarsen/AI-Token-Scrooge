# AI Token Scrooge: Unified Reset Horizon History

Planning date: October 29, 2026. Status: implemented and verified; see docs/verification.md for acceptance evidence and platform coverage limits. Later user corrections below supersede the original graph-value and gap requirements.

The selected visual reference is [history-selected.png](../design/references/history-selected.png). This feature plan supersedes earlier history concepts and the initial idea of separate 5-Hour / Weekly selectors. Preserve the current production dashboard and subsequent refinements wherever the generated reference differs.

## Outcome and Scope

Extend Reset Horizon into one continuous timeline with seven days of recorded usage behind Today and the existing upcoming reset/refresh events ahead. Reuse the slim purple navigator, event dots, rounded panel, header, settings shortcut, provider icons, badges, and popup styling.

Show one history line per eligible account:

| Provider | History Series |
| --- | --- |
| OpenAI Codex | Weekly |
| Claude | Weekly |
| GitHub Copilot | Monthly |

5-Hour and Current Session limits remain on the main dashboard and retain their alerts. They do not appear as history lines or historical refresh markers. Their existing future refresh events remain on the future horizon. History selections affect chart visibility only, never monitoring or alert settings.

## Interaction Contract

- Add a themed History toggle to the Reset Horizon header, enabled by default. The initial position remains Today, with upcoming events to the right. Enabling History does not jump backward automatically.
- Drag the existing navigator left to reveal history. It remains a single continuous pan across past and future, not a separate history screen or second slider. No arrow buttons or dates below the navigator.
- With History enabled, target a seven-day visible span at desktop width so a full leftward pan shows the last seven days through Today. Continue panning right to the furthest known future event. At narrow widths, retain readable day cells and show fewer days at once rather than compressing labels into collisions. With History disabled, preserve the existing future-only fit/scroll behavior.
- Keep a clear Today boundary; historical lines stop at the latest valid observation and never project into the future. Use one shared timestamp projection for day ticks, curves, event markers, and navigator dots. Every calendar day has equal width in the selected time zone, including across daylight-saving changes.
- Keep a fixed compact selector rail on the left in History mode. One provider icon per account, with nickname/plan badge underneath or beside it. Clicking the icon toggles that account's line; selected icons are purple and unselected icons are muted. Hide an unselected line completely. No 5-Hour / Weekly sub-selectors.
- Persist account visibility with automatic saving. Distinguish duplicate providers by account ID, nickname, and plan; names are display labels, not series identity. Give each selected account a stable, distinct line color, with a matching swatch and endpoint label. These colors identify accounts and do not indicate alert severity.
- Label the vertical axis `% Remaining`, from 0% to 100%. Reuse the reference's restrained grid, thin lines, light area fills, and compact endpoint labels. Values normally fall with use and rise when replenishment is observed. The generated reference's reversed slopes are not the data specification.
- Hover or click a point/event for an immediate themed popup containing the account, weekly/monthly period, remaining value, timestamp, and available event details. Keyboard focus must expose the same information and selector actions. Endpoint labels must avoid overlaps.
- Refreshes, auto-saving selectors, and account reordering must not recreate the horizon or reset its scroll position. If the user stays at Today, keep that anchor as time advances; after they pan away, preserve the viewed dates. Turning History off returns to Today. Account removal and resize clamp the viewport only when necessary.

## Data and Event Truth

Source inspection confirms that `LocalStore` already saves full quota snapshots and retains them for 30 days. Successful monitoring polls write history; initial connection snapshots currently do not. The existing `HistoryAsync` reads the latest 10,000 records without date bounds. Actual live history coverage has not been measured.

1. Reuse the existing database and retention policy. Add a chronological, bounded read for the displayed historical interval, including the immediately preceding observation when needed for continuity. Record the first accepted connection snapshot through the same history path. Persist accepted connection state and its first history record in one database transaction; failed/rejected connections must not leave historical samples. On a database write failure, retain the prior persisted state and surface the error without claiming successful history capture. Query asynchronously and discard obsolete results after account, identity, time-zone, or range changes.
2. Select eligible windows by provider semantics, stable ID, and duration, not localized display text or primary/secondary position. Codex and Claude weekly duration is 10,080 minutes; Copilot has its monthly window ID. Internally key observations by account ID and window ID, and validate snapshot subject/scope. Never merge unrelated weekly buckets or stitch incompatible identity/allowance changes. Resolve ambiguous multiple weekly buckets explicitly during implementation rather than silently averaging them.
3. Plot only known, valid remaining percentages. Use gaps for unavailable allowance, missing windows, identity mismatch, stale observations, or a gap longer than the greater of 15 minutes and three scheduled polling intervals. Show partial history honestly; do not backfill before the first sample or extend a stale last sample to Today. Keep timestamps for original observations in popups.
4. Preserve measured replenishment values. For example, a move from 60% to 91% remains 60% to 91%, not an invented jump to 100%. A step transition can be drawn at the first observation after the increase; details must state that the transition was observed between polls, not known to happen at that exact instant.
5. Derive prior scheduled refreshes and credit expirations from stored snapshots. Deduplicate unchanged schedules and reconcile revisions within a quota cycle so repeated polling does not multiply markers. Use purple refresh and amber expiry icons, with the associated account in details. Selected historical accounts determine which historical markers are visible; existing future event/count behavior remains unchanged.
6. A credit expiration is not a quota refill. `ResetCredit` has no redemption timestamp, and alert history records reminder delivery, not completed resets. Do not infer `Reset Used` from a disappearing credit, a reduced reserve count, or a capacity increase. Label historical schedules as scheduled/reported events; only label an actual reset use when authoritative evidence becomes available.
7. Limit drawing work to the viewport and downsample by horizontal pixels while preserving gaps, extrema, and replenishment transitions. Keep unmodified observations for popups. No new chart package or backend is needed for the initial implementation; build on the existing MAUI graphics controls.

## Integration Points

| Existing Code | Planned Responsibility |
| --- | --- |
| `Infrastructure/LocalStore.cs` | Time-bounded historical snapshot read |
| `App/MainPage.Actions.cs`, connection path | Accepted initial and subsequent snapshot capture |
| `Core/Quota.cs`, provider mappings | Existing identity, period, remaining-percent, schedule semantics |
| `App/Presentation/` | Pure history-series, event, and viewport projection models |
| `App/Controls/HorizonView.cs` | Reuse header/navigator and host historical/future rendering |
| `App/MainPage.cs` | Retain the horizon instance or view state across dashboard updates |
| Settings persistence | Auto-save history visibility and selected account IDs separately from alert rules |

Keep the shared date projection and viewport state independent of drawing code so the scrolling behavior can be verified without native input automation. Follow the existing `ActionButton`, provider icon, tooltip, and accessibility patterns.

## Delivery and Verification

Execute the ordered task cards in [history-todo.md](history-todo.md). Data projection and viewport math can be developed in parallel once their small shared contracts are agreed; integration is sequential. Commit and push each completed increment under the user's standing instruction. After every UI/runtime increment, rebuild and restart the current Windows app using the established launch path and existing account store, then verify the visible change before reporting completion.

Acceptance requires the selected visual reference plus current production styling at representative desktop and narrow widths, including Windows display scaling. Verify the full seven-day backward pan, future pan beyond 30 days, equal day spacing, multiple same-provider accounts, selector persistence, immediate popups, event semantics, offline gaps, resize, refresh while panned into history, and account reordering. Existing usage bars, threshold controls, alerts, and sign-ins must continue to work.

This planning pass used source inspection and a separate read-only data audit. It did not query live history coverage, run tests, change account settings, build, or restart the app. Implementation and native visual verification remain pending.

## Goal Handoff

Implement the unified Reset Horizon history described in `tasks/history-plan.md` and `tasks/history-todo.md`, using `design/references/history-selected.png` as the visual reference. Show one Weekly line per OpenAI/Claude account and one Monthly line per Copilot account; retain all existing dashboard quota bars. Start at Today and reveal seven days of real history only when scrolling left with the existing purple navigator. Preserve honest gaps and provider-reported event semantics. Complete the task-card verification, commit and push each completed increment, and rebuild/restart/visually verify the running Windows app after UI/runtime updates. Ask before changing unrelated behavior.

## Approved Follow-up Corrections
- Use % Used for the history chart, tooltips and endpoint labels.
- Linearly interpolate missing observations between compatible known samples and identify estimates; preserve identity/allowance discontinuities.
- Refresh plays one quack; automatic polls stay silent.
