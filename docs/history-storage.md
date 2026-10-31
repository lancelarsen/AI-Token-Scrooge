# Compact Usage History

Scrooge records every successful provider reading. Equal consecutive states share a run containing the first and last measured timestamps, first/last provider timestamps, observation count, polling cadence and maximum observed gap. An unchanged state includes every quota window's numeric usage, allowance and reset timestamp, plus plan, identity, source health and reset-credit metadata.

A run stops when any state changes, polling cadence changes, a known reset/expiration boundary is crossed, or the gap exceeds three polling intervals (at least 15 minutes). Sub-second imports stay separate. This preserves changes, peaks, drops, allowance transitions and reported reset schedules. The graph receives the measured endpoints and coverage information; it does not create intermediate observations.

## SQLite Version 3

- `history`: account/first timestamp key, last timestamp, monotonically increasing revision and a compact numeric payload. `W` holds arrays of usage, allowance and reset UTC ticks. `D` references shared metadata; `F/L`, `P/Q`, `N`, `C` and `G` store observation endpoints, provider endpoints, count, cadence and maximum gap.
- `history_definitions`: shared metadata, including window names/units, plan, identity and reset-credit details. These are stored once per distinct account state rather than repeated in every reading. Decimal values round-trip through JSON numeric values without SQLite floating-point conversion.
- `history_clock`: a durable revision cursor. Account/revision, account/last-time and expiry indexes support incremental loading and cleanup.

The upgrade creates `radar.db.before-history-v3.bak` using SQLite's backup API, then converts rows in a transaction while streaming legacy data through SQLite. Failed conversion rolls back both schema and data. After conversion, SQLite reclaims free pages where possible. The backup is retained separately for rollback; an older executable requires that pre-upgrade database, since it cannot read version 3.

Ordinary history retention still follows the configured application policy (currently 45 days). Runs overlapping the cutoff retain their real first endpoint as a continuity anchor. Account removal deletes both runs and shared metadata. The migration backup is a separate recovery file and is not included in ongoing retention cleanup.

## Graph Loading and Drawing

The first load reads the visible seven-day range and a preceding anchor. Later loads query only runs whose revision changed. An extended run replaces its former cached endpoint, while a new state appends a run. The immutable cache is adopted only after cancellation/signature checks, so an older asynchronous load cannot overwrite newer results. Identity changes or requests for an earlier uncached range force a fresh bounded load.

The graph still uses screen-pixel reduction with first/last/minimum/maximum values per bucket. Segment boundaries, replenishments and estimated-span edges are retained. Hover retains the original measured endpoints; intervals between observations remain estimates. Existing equal-day calendar geometry and exact timestamp event positions are unchanged.

## Verification

The compact-history tests cover repeated and slow-cadence readings, all quota windows, exact decimals, changes in allowance, reset/expiration events, gaps, extended cached endpoints, old-database backup and migration rollback. Existing reduction tests retain extrema and reset/gap boundaries and confirm that a narrow viewport does not project all off-screen samples.

An audit of a fresh copy of the local database on October 31, 2026 preserved 8,053 observations in 3,434 runs. All 8,048 graph-eligible observation values and both historical reset events matched the original data. SQLite integrity check returned `ok`. The database copy decreased from 8,830,976 to 1,396,736 bytes; numeric payload plus shared metadata decreased from 6,731,354 to 503,999 bytes. Counts grow as monitoring continues.
