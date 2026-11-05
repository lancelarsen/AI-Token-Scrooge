# Repository Instructions

## Required Contributor Guides

Before implementation, read [Contributing](CONTRIBUTING.md), [Application Architecture](docs/architecture.md), and [Testing and Verification](docs/testing.md). Read the additional guides for the work being changed:

- UI, layout, controls, icons, interaction, or copy: [Design Guidelines](docs/design.md)
- Mac visual parity and cross-platform UI review: [Windows UI Reference](conversion-for-mac/mac-ui-parity.md) and [Screenshot Gallery](conversion-for-mac/mac-ui-gallery.md)
- Providers, browser/CLI sign-in, usage mapping, polling, or credential lifecycle: [Provider Integration](docs/integration.md) and [Security](SECURITY.md)
- Credential failure, removal, or revocation guidance: [Credential Recovery](docs/credential-recovery.md)
- Persistence, retention, or migrations: [History Storage](docs/history-storage.md) and the architecture persistence section
- Build, packaging, or releases: [Release Acceptance](docs/release-acceptance.md) and [Windows Preview Workflow](.github/workflows/windows-preview.yml)
- App versions, update checks, or restart handoff: [App Versions and Updates](docs/app-updates.md)
- New features and provider proposals: [Contribution Roadmap](tasks/contributor-roadmap.md)

These guides are implementation requirements, not optional background reading. Current user instructions take priority. The current design guide supersedes conflicting historical concept notes. When code and a guide disagree, identify the discrepancy and resolve it within the task rather than silently creating a new pattern.

## Architecture and Integration Boundaries

- Keep domain logic in Core, external I/O and persistence in Infrastructure, and presentation/Windows orchestration in App. Use the existing shared controls.
- Monitoring must not send inference prompts, redeem resets, buy credits, or switch accounts in other applications. Prefer provider-owned browser sign-in or an isolated official CLI profile.
- Verify Subject and Scope, preserve nullable unknowns and allowance provenance, honor cancellation/retry deadlines, and reject stale results from removed or reconnected accounts.
- Persist account changes before credential retirement; cleanup must remain retryable. Never put credentials or raw provider responses into logs, fixtures, screenshots, SQLite state, or release artifacts.
- Add focused regression coverage for changed behavior. Report fixture, native, and live evidence separately. Update the relevant guide and roadmap when behavior or architecture changes.
- When the user changes planned work or contributor scope, update README, CONTRIBUTING, the active roadmap, and affected cross-references together. Keep required verification for actual changes distinct from standalone future projects.
- Preserve unrelated working-tree changes. Do not mark a research-only provider supported or mark unverified acceptance work complete.

## UI Copy
- Use Title Case for short UI titles, labels, tooltips, and concise status messages, capitalizing the principal words (for example, "Report Accounts").
- Do not add a trailing period to short UI strings. Keep full explanatory sentences in sentence case with normal punctuation.

## README and Documentation Copy
- Use Title Case for headings, table headers, navigation labels, standalone short labels, and named UI controls in README and documentation. Do not add trailing periods to these labels. Keep explanatory sentences and descriptive table content in sentence case.
- This project preference overrides any writing skill's sentence-case heading default. Preserve approved taglines exactly.
- Before publishing documentation edits, review every heading, table header, navigation label, and short label for capitalization, then check Markdown formatting and internal heading links.

## Completing a Change

After each requested change, run the relevant checks, commit the files for that change, and push the commit to GitHub. Report the commit and any verification gaps. Leave work local only when the user asks or a blocker prevents the push.

## Running App
- After each UI or runtime behavior update, rebuild and restart the running Windows app from the latest build, then confirm the visible app reflects the change before calling it complete. A successful build alone does not update an already-running instance.

## Reset Horizon Geometry
- History on/off must use the same timeline renderer for day headings, rail, circles, future events and navigator. Toggle only the history content; do not recreate matching visuals through separate native and canvas drawing paths.
- Consecutive calendar-day markers must always be equally spaced, including Today and the first/last visible days. Compact all day widths together as the range grows; never reposition or clamp one day independently.
- Day labels, timeline dots, and grouped service icons must share the same day anchor. Preserve spacing during scrolling, resizing, history toggles, and daylight-saving transitions. Cover rendered day-anchor geometry with regression tests.
- Historical event markers and usage samples must share the actual timestamp scale. Center day headings in calendar-day buckets; never offset them by the current time of day or average nearby event timestamps.
