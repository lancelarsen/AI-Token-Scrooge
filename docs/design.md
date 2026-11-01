# Design Guidelines

AI Token Scrooge helps people decide which account has capacity and when to use it. Every provider must feel like part of the same Windows app.

This is the current contributor design specification. It supersedes conflicting early concepts in [Design Selection](../design/selection.md). Use the running app and the source references below together: screenshots illustrate the appearance, while source controls define behavior. If the checkout differs from an approved rule here, identify the gap instead of introducing another competing pattern.

## Brand and Visual References

- Product name: AI Token Scrooge
- Tagline: Never Waste a Token! Never Miss a Reset!
- Mark: [Token Baron](../src/AIResetRadar.App/Resources/Images/token_baron.png)
- References: [Dashboard](images/dashboard.png), [Account Rows](images/accounts.png), [Settings](images/settings.png)

These screenshots contain labeled demonstration data. They are layout references, not evidence of live provider access. Do not replace the product mark or style each provider as a separate branded application. Provider logos identify accounts and events.

## Surfaces, Colors, and Typography

Values below come from existing controls and are reference values, not a new theme implementation. Reuse the corresponding controls before copying their constants.

| Role | Current Treatment | Source |
| --- | --- | --- |
| App Background | Charcoal gradient, `#1B1C20` to `#141519` | `MainPage.cs` |
| Cards | `#1C1E24`, border `#383B46`, rounded corners | `MainPage.cs`, `Views/SettingsPage.cs` |
| Action Controls | `#232429`, border `#45464D`, primary text `#F2F1F5` | `Controls/ActionButton.cs` |
| Brand and Selection | Lavender, including `#C3ADF8` and `#B5A0F4` | Header and shared controls |
| Secondary Text | Muted gray-lavender, including `#B6B8C8` and `#A5A8BA` | Header and settings descriptions |
| Normal Capacity | Green gradient `#208455` to `#4DCE8A` | `Controls/UsageBar.cs` |
| Info Capacity | Blue gradient `#2367B5` to `#58A6FF` | `Controls/UsageBar.cs` |
| Warning Capacity | Yellow gradient `#AE8519` to `#F2C94C` | `Controls/UsageBar.cs` |
| Critical Capacity | Red gradient `#B52D42` to `#F05B69` | `Controls/UsageBar.cs` |

Use Segoe UI and Segoe UI Semibold for the established Windows typography. Current reference sizes are 32 for the brand title, 26 for the settings page heading, 21 for settings section headings, 16 for usage labels, 14 for action text, and 12-13 for supporting details. These are logical units, not screenshot pixels.

Keep color meanings distinct: lavender marks quota refreshes; amber marks earned-reset expirations; capacity severity uses green/blue/yellow/red. Account history-line colors identify accounts, not severity. Never rely on color alone to communicate health, selection, or an alert.

## Layout and Shared Controls

The dashboard order is brand/actions, Reset Horizon, account sorting and Add Account, then account cards. Preserve this hierarchy when adding providers.

Reference spacing is 32 horizontal dashboard padding, 20 between dashboard sections, 16 inside settings cards, 12 between settings cards, and 8 within a settings card. Settings content caps at 960 logical units and contracts with the window. Match the nearest existing layout rather than adding arbitrary spacing.

| Need | Reuse |
| --- | --- |
| Icon and Text Action | [ActionButton](../src/AIResetRadar.App/Controls/ActionButton.cs) |
| Account and Quota Rows | [AccountQuotaView](../src/AIResetRadar.App/Controls/AccountQuotaView.cs), [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs) |
| Capacity Fill and Labels | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| Per-Window Alert Controls | [CapacityAlertToggle](../src/AIResetRadar.App/Controls/CapacityAlertToggle.cs), [AlertThresholdView](../src/AIResetRadar.App/Controls/AlertThresholdView.cs) |
| Timeline and History | [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| Settings Cards and Forms | [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| Help and Dialogs | [SettingsHelp](../src/AIResetRadar.App/Controls/SettingsHelp.cs), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs) |

ActionButton uses a 44-unit height, 10-unit corner radius, and a lavender icon to the left of its text. Settings cards use 15-unit corners. Usage bars use a 48-unit height and 11-unit corners. Keep icon-only actions accessible by name. Use existing Fluent/Lucide icon treatments and record licenses for new assets.

## Account Cards and Capacity

Group all quota windows for one account in one card. Keep provider identity, nickname, and plan together. Each window gets its own bar, alert controls, reset countdown, and exact reset date. Do not combine session and weekly allowances or duplicate Used/Left values in an extra column.

Bar length represents used capacity. Keep both Used and Left labels readable over the entire track at 0%, 100%, and overage. Display dashboard percentages as whole numbers; retain underlying precision for arithmetic. Unknown usage is not 0% usage. Stale data must not appear as a newly confirmed healthy reading.

Custom Order uses the selected six-dot grip treatment. Preserve the user's ordering, filtering, and selected controls during background refreshes. A new provider should use the existing card instead of introducing a provider-specific dashboard layout.

## Reset Horizon Invariants

- History on/off uses one timeline renderer for day headings, rail, circles, future events, and navigator. Toggle only the historical content.
- Keep service selectors visible. An unchecked account filters its history and future events. With History off, users cannot navigate before Today.
- Consecutive calendar-day anchors are equally spaced, including Today and both endpoints. Compact widths together; never move one label or day independently.
- Day labels, dots, and grouped service icons share the same day anchor. Preserve alignment during scrolling, resizing, filtering, toggling, and DST transitions.
- Center day headings in calendar-day buckets. Place historical samples and events on their actual timestamp scale; do not shift them by the current time or averaged timestamps.
- A refresh and an earned-reset expiration are different events. Preserve their colors, labels, details, and alert schedules.
- Show gaps in history honestly. Do not draw continuous usage through missing observations. Use step-like real observations rather than inventing smooth consumption curves.

Changes to this geometry need regression coverage and a rendered comparison. Start with `HorizonViewport`, `HistoryChartLayout`, and their tests.

## Settings and Help

Keep global defaults separate from account-specific settings. Settings save through the existing persistence flow; show failures and do not report success before persistence completes.

Use grouped charcoal cards with lavender headings. Prefer one useful section tooltip over repetitive info icons. Add individual explanations when controls mean different things, such as Polling Interval, Monitor This Account, Capacity Exhausted, Capacity Restored, and Reset Expiry Reminders. Do not explain an obvious label again in a tooltip.

Polling intervals are whole minutes from 1 to 30, with vertical minute markers and a thumb that snaps to their positions. Verify actual dragging, not just the rounded displayed number. Line colors use square swatches; custom colors append to the palette, followed by a matching square + button that opens the color chooser. Preserve the selected color and saved palette.

Quiet Hours uses the existing two-handle range. Retention uses the existing picker and defaults to 90 days. Shortening retention must explain that older records will be removed.

## Copy and Accessibility

Use Title Case for headings, short labels, tooltips, and concise status messages, without trailing periods. Full explanatory sentences remain sentence case. Preserve the approved tagline exactly. Examples: What Scrooge Shows; Sign In with GitHub; Polling Interval.

Keep keyboard focus visible. Give controls semantic names and values; exclude decorative icons from redundant screen-reader output. Provide keyboard access to custom controls and readable labels for severity. Test long nicknames, unknown plans, multiple windows, empty accounts, offline/auth errors, and large text. Do not let a picker, swatch row, or dialog clip its final action.

## Visual Acceptance

Before editing, identify the closest production control and name it in the PR. After editing, rebuild and restart the Windows app from the new output. Compare the actual app at a representative desktop size and the 800-by-700 compact preview, including Windows scaling. Check navigation, keyboard focus, empty/error states, and long content. Include redacted screenshots and state any unverified cases. Compilation alone does not establish visual correctness.
