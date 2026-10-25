# Implementation evidence and remaining gates

Updated October 25, 2026. Goal remains active. This report distinguishes implementation and fixtures from live authenticated verification.

## Verified this build session

- Native Windows MAUI project builds with zero warnings/errors after the initial implementation slices.
- Core tests: 16 passed. Provider/storage tests: 8 passed. Scope includes percentage/absolute/unknown usage, overage, countdown confirmation, partial reset reserves, quiet hours, warning severity, stable expiry-event keys, workspace mismatch, Copilot units and allowance provenance, authenticated GitHub user binding, organization user filters, and real SQLite restart round-trips.
- Installed Codex schema generated with 0.164.0-alpha.11.1. It includes multi-bucket quota windows, earned reset count/details, optional workspace identity and included-usage permission.
- A real stdio protocol probe used two different **empty app-owned profiles**. Both returned no signed-in account, establishing that these profiles did not import the user's existing login. Inference methods were rejected by the client allowlist. This is not a two-signed-in-account renewal test.
- The real app was launched and inspected through native Windows Computer Use. Dashboard, all four sample quota rows, timeline, Settings, and named accessibility controls were observed. Source references: CreatorMicroModelKeys editor/settings screenshots and the exact user-selected `design/references/approved-ui.png`.
- A native UI click on **Send test notification** created a persisted alert. Windows returned a nonzero notification ID; the test database reported one accepted notification. That establishes API acceptance, not proof of a visible popup when Windows suppresses banners.
- A self-contained Release x64 publish completed. Subsequent UI refinements require republishing before delivery.

## Observed visual differences / follow-up

- The initial desktop window extended beyond the visible monitor working area; startup now clamps it to the available area. Retest this change.
- Timeline includes actual chronological events, including the second account's earlier reset; it intentionally does not copy the mockup's unsorted account rows or omit that event.
- A horizontal scrolling timeline is being used at narrower widths to avoid consuming several rows of the compact screen. Native compact inspection remains required after this change.
- Native title bar, real provider icons, demo labels, unknown-data labels and actual values differ from the generated image. Preserve the selected visual hierarchy while testing actual native layout, keyboard access and scaling.

## Still required

- Authenticated Codex account/window/reserve comparison; two independent signed-in accounts, token renewal, sign-out/remove isolation and restart.
- Authenticated GitHub personal/organization access reconciliation, allowance provenance and billing latency.
- Final desktop and 800×700 native comparison, keyboard/Narrator and 100/150/200% DPI verification.
- Notification View/Snooze/Dismiss and cold-start activation, alert history restart behavior, disabled OS notifications, quiet hours and source-change reconciliation.
- Tray quick view, hide/restore/Exit, Explorer restart, network/power/clock recovery, resource measurements and clean-user package smoke test.
- Snapshot history/retention, reconnect UX, stronger fault/race coverage, serving-version update assessment and final code review.
- Reproducible distribution package and README acceptance check against final behavior.

No required live check is considered passed because demo fixtures or a build succeeded. No credential contents were inspected or copied into reports.
