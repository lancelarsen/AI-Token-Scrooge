# App Update Implementation

## Accepted Scope

- Date-based versions in Central Time, with a durable daily counter
- Version above Time Zone in Settings
- Persisted Check for Newer Versions switch, default on; eight-hour checks and Check Now
- Get Update, bounded verified download, then Update and Restart
- Stable shortcuts/startup, preserved data directory, no rollback feature
- Newest downloadable release only, with version reservations retained
- Document update network traffic and trust limits

## Evidence and Remaining Work

- [x] Add version comparison and eight-hour scheduling tests
- [x] Add release selection, download, package-path, activation, and preservation tests
- [x] Run full test suite: 425 passed
- [x] Compile Windows app and standalone launcher
- [x] Verify portable ZIP and metadata: 639 files, app/launcher publish passed, matching assembly and package version
- [x] Verify actual Settings at desktop and compact sizes: card above Time Zone, no clipping at 800 by 700
- [x] Verify saved update preference after reopening: Off persisted in SQLite and remained Off when Settings reopened
- [ ] Exercise actual download and helper restart, including original shortcut and data-directory arguments
- [ ] Verify GitHub publication, durable version tag, and old-release cleanup
- [ ] Record native evidence and finish the update documentation

The test and compile results above do not establish the unchecked native or release behavior. No rollback feature is planned.

## Native Checkpoint

The November 3 local build used `artifacts/publish/app-updates-verified-win-x64`, version `v2026.11.3.000`. The package scan found 639 files and a self-contained launcher. ZIP SHA-256: `754DBE56099D823671DBE8D3413F73C57522AECF3168419290061BE7D70D6B79`. This is a local working-tree build, not the final published release.

The production Settings cards were the visual reference. The new card appeared above Time Zone at desktop and 800-by-700 sizes without clipping. The normal app was restored using its existing data-directory argument, loaded the saved accounts, and successfully checked GitHub. The manual check and full release handoff are tracked separately from fixture evidence.
