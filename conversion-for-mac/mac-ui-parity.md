# Windows UI Reference for the Mac Port

Use this guide, the [Screenshot Gallery](mac-ui-gallery.md), and the [Design Guidelines](design-reference.md) together when matching the Xcode app to AI Token Scrooge. Screenshots establish appearance; the interaction notes and linked source establish behavior. Use the shared chat for comparisons and questions, but keep agreed changes in these documents so another checkout can reproduce the decisions.

## Capture Baseline

- Captured November 4, 2026 from the running Windows app in `artifacts/publish/timeline-wheel-win-x64`.
- Base commit: `fc37458a2d5ec5d3ef7c1f321ad43304b0a8ffc2`. The working tree also contains the newer button-state, shared-purple, immediate-feedback, and timeline-wheel fixes. These captures are not a claim that the published GitHub release includes those fixes.
- Images 01-08 show the normal application with saved settings. Images 09 onward use isolated demo or empty data. Demo identities are synthetic; no sign-in was performed for this reference.
- Desktop captures are 1463 by 866 pixels as returned by the capture tool. Compact captures are 789 by 694, from the app's `--compact` launch option. These are capture dimensions, not a specification for Mac window size or logical layout units.
- The blue glow around the pointer, and the small blue strip sometimes visible at the top edge, are capture/desktop artifacts. Do not implement them.
- No runtime code was changed for this capture pass. The normal account session was restarted after the isolated captures.

## How to Compare the Two Apps

1. Use the same synthetic accounts, quota values, selected filters, and viewport in both apps. The Windows fixture is [DashboardState](../src/AIResetRadar.App/Presentation/DashboardState.cs), with history in [HistoryDemo](../src/AIResetRadar.App/Presentation/HistoryDemo.cs). Dates in this gallery advance with the capture clock; use a fixed clock for repeatable automated comparisons.
2. Compare equivalent content widths in logical units. Compare desktop and compact layouts separately; Retina pixels are not Windows logical units.
3. Review one surface at a time: dashboard, timeline, account rows, global settings, account settings, dialogs, then connection flows.
4. For every surface, compare normal, hover, focus, selected, processing, disabled, empty, and error states where applicable. A screenshot of the normal state is not evidence that the interaction works.
5. Record each difference as Match, Intentional Mac Adaptation, Fix Needed, or Not Yet Verified. Link the Windows image number and the Mac capture in the shared chat.

Keep the Scrooge content area close to the reference. Use native macOS window controls, application menus, notifications, menu-bar presence, login-item management, and credential protection. Those adaptations should preserve the user outcome. Do not recreate Windows title-bar buttons or port Windows credential/update helper code into the Mac app.

## Visual Foundation

The complete color inventory remains in [Design Guidelines](design-reference.md#color-roles-and-exact-values). Avoid sampling compressed screenshots to reconstruct colors.

| Element | Required Treatment |
| --- | --- |
| Brand | Existing Token Baron mark, AI Token Scrooge name, and exact tagline: Never Waste a Token! Never Miss a Reset! |
| Main Surface | Charcoal gradient `#1B1C20` to `#141519` |
| Cards | `#1C1E24`, `#383B46` border, 15-unit corners |
| Normal Action | `#232429`, `#45464D` border, `#F2F1F5` text, lavender icon |
| Hover and Processing | Solid `#483A64`, white text and icon; processing remains fully colored and non-clickable |
| Selected Sort, Row Alarm, Navigator | The same `#483A64` interaction fill; do not introduce the retired darker purple `#48306F` |
| Refresh Events | Lavender `#B5A0F4` |
| Earned Reset Expirations | Amber `#EAAA51` |
| History Colors | Account identity, not capacity severity; custom colors stay with the account |
| Modal Scrim | Black at alpha 160/255; dashboard remains visible but cannot receive input |
| Typography | Windows uses Segoe UI / Semibold. Match hierarchy and metrics on Mac with a suitable system font; do not rely on Windows-only glyph fonts |

Reference typography is 32 for the brand, 26 for page headings, 21 for settings section headings, 20 for provider names/countdowns, 16 for usage labels, 14 for actions, and 12-13 for supporting details. Dashboard horizontal padding is 32, section spacing 20, settings-card padding 16, settings-card spacing 12, and inner spacing 8. Actions are 44 high with 10-unit corners. Usage bars are 48 high with 11-unit corners.

Reuse the actual [image resources](../src/AIResetRadar.App/Resources/Images) and preserve their aspect ratios. Use vector equivalents for Fluent/Lucide-style utility icons on Mac. The Custom Order icon is the six-dot grip, not a grid or list icon. Match stroke weight and baseline across icons. Keep provider logos recognizable without turning each account card into a separate provider theme.

Short labels and headings use Title Case. Full explanatory sentences use sentence case. A few older labels in the captures still use sentence case; follow the current design rule rather than reproducing those inconsistencies.

## Dashboard and Account Rows

References: images 01, 09, 12, 19, 26, 27, 33, 34.

- Keep the order: brand and actions, Reset Horizon, sorting and Add Account, then account cards. The brand/actions header remains visible while the main content scrolls vertically.
- At narrower widths, actions move below the brand. The Windows header switches at 1100 logical units. Account content switches from side-by-side to stacked at a card width of 1050. Use the resulting layouts as the reference, allowing for Mac text metrics.
- One card represents one account. Provider logo/name, nickname, and reported plan identify it. Multiple quota windows appear inside that card with their own bars, alarms, and reset countdowns.
- A bar fills according to percent used. Used is on the left, Left on the right; both remain readable over the full track. All dashboard percentages use whole numbers, including GitHub. Keep precision in the model.
- Capacity colors: green normal, blue informational, yellow warning, red critical/exhausted. Image 12 contains all four. The gradients are listed in the design guide. A user's account-line color must not replace these severity colors.
- Threshold handles represent remaining capacity, although the bar measures used capacity. Default Info is 50% left, Warning 30%, Critical 10%, so their visual positions are at 50%, 70%, and 90% of the used track. Dragging shows a severity-specific value tooltip. Keep Critical below Warning and Info at or above Warning.
- Row alarms are a two-part bell/muted-bell control. Muting one quota does not mute every quota in that account. The selected half stays purple. In image 33, muting hides that row's threshold markers while retaining its usage bar.
- Countdown clock color can reflect the due refresh reminder independently of the usage bar's severity. Keep both the relative countdown and exact date/time.
- Clicking the bar body, quota description, or reset details opens usage details. Dragging a threshold must not also open that dialog.
- Custom Order enables dragging account handles. The other segments are Most Remaining, Least Remaining, and Next Reset. Refreshes must preserve the selected sort and must not interrupt an active drag or threshold edit.
- Unknown usage is not zero. Distinguish stale, offline, authentication, permission, unsupported, loading, and demo states. Preserve the last valid data without presenting it as freshly confirmed.

Source: [MainPage](../src/AIResetRadar.App/MainPage.cs), [AccountQuotaView](../src/AIResetRadar.App/Controls/AccountQuotaView.cs), [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs), [AccountSortControl](../src/AIResetRadar.App/Controls/AccountSortControl.cs), [ThresholdEditor](../src/AIResetRadar.App/Platforms/Windows/ThresholdEditor.cs).

## Reset Horizon

References: images 09-11, 26, 35-37.

- The header contains the history icon/title, refresh and expiration legends with counts, History switch, contextual hint, and settings gear. The gear opens global Settings at Earned Reset Expiration Alerts; it is not a separate timeline-appearance page.
- Keep the checkable service rail on the left at all times. Each selector has provider logo, nickname badge above, plan badge below, account-colored outline, and a lower-right selection box. Unchecked selectors remain visible with reduced emphasis.
- An unchecked service removes both its history and its future refresh/expiration events. Update legend counts accordingly. This is a display filter, not a monitoring switch.
- History defaults on and the choice persists. Turning it off hides historical content and prevents scrolling before Today. Future events, day headings, rail, dots, service selectors, and navigator remain. Use one renderer rather than two almost-matching timelines.
- All consecutive calendar days are equally spaced. Center headings in calendar-day buckets; keep dots and grouped future icons anchored to the same day. Today has the larger filled circle. Do not independently clamp the first or last marker.
- Historical samples/events use actual timestamps within those calendar buckets. Keep step-like observations, reset drops, and gaps. Do not replace history with smooth curves or arbitrary diagonal lines.
- Future refresh icons are lavender; expiration icons are amber. Stack multiple events within a day and retain plan badges and exact times. Historical events use smaller circular markers on the rail.
- Wheel input over the chart, headings, and event icons pans horizontally. Consume it even at the boundary so the dashboard does not unexpectedly move vertically. The latest Windows fix routes wheel events through the common chart host. The selector rail has its own vertical overflow behavior.
- The bottom overview has event dots, a purple viewport thumb, a small grip, and a current-position indicator. Dragging the thumb pans; clicking outside it recenters; keep dragging stable even when the pointer leaves the thin track.
- Hover a sample for details; click to pin it. Show account, quota window, value, and timestamp, with a sample marker and guide. Escape closes the readout. Hide stale readouts on scrolling/resizing.
- Keyboard on the chart: arrows inspect samples, Enter/Space pins details, Page Up/Down moves seven days, Home returns to Today. On the navigator, arrows move one day. Keep these functions accessible using Mac-appropriate key equivalents where needed.
- Event hover shows provider/account/plan, window, event type, and time. Clicking a refresh opens usage details; clicking an expiration opens available-reset details. Neither action claims a reset.

Source: [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonViewport](../src/AIResetRadar.App/Presentation/HorizonViewport.cs), [HistoryChartLayout](../src/AIResetRadar.App/Presentation/HistoryChartLayout.cs), [HistoryChartInput](../src/AIResetRadar.App/Platforms/Windows/HistoryChartInput.cs).

## Buttons, Panels, and Feedback

References: image 01 shows Refresh processing; 08 shows a hovered history action; 20-23 show dialogs and form actions; 32 shows an alert banner.

- Normal actions have an icon followed by text. Hover changes the entire action to solid purple with white foreground. Pointer exit restores the prior normal or selected appearance.
- Processing uses exactly the hover colors, an animated white progress ring, and an action-specific label such as Refreshing or Checking with an ellipsis. Disable repeated activation until completion/failure. Show this state immediately on click, before sound playback, provider initialization, disk work, or network work. Let the UI render before starting expensive work.
- Restore the original label/icon after completion and handle failure without leaving the action stuck. A static screenshot cannot demonstrate animation or timing; test both in the Mac implementation.
- Large panels are centered inside the main window with a dimmed dashboard. Windows caps them at 1000 by 800 logical units, with at least 24 horizontal and 32 vertical units around them. Settings content caps at 960. Connect forms cap at 640.
- Settings has a fixed title/Close row and a scrolling body. Keep the final action reachable on compact windows. Focus stays within the modal; closing restores previous focus. Escape follows the same save/cancel behavior as the Close/Cancel action.
- Settings autosaves. Close flushes pending edits and waits for saving; it must not conceal a failed save. The user reported a slow Windows Close response; no measured root cause or fix is established. Preserve safe saving on Mac without intentionally reproducing that delay.
- Use themed small dialogs for details, choices, and confirmation. Do not replace a detail panel with an unstyled system alert just because the text fits.

Source: [ActionButton](../src/AIResetRadar.App/Controls/ActionButton.cs), [AppPalette](source/AppPalette.cs), [AppPanels](../src/AIResetRadar.App/Services/AppPanels.cs), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs).

## Global Settings

References: images 02-08 and 31. Keep this section order:

| Section | Controls and Behavior |
| --- | --- |
| App Updates | Version first; automatic-check switch; status and last-check text; Check Now; conditional Get Update, then Update and Restart after download. Checks are roughly every eight hours while running. Mac must request a compatible Mac build, never the Windows ZIP. |
| Time Zone | One full-width picker, used for dates, timeline labels, reset times, and quiet hours |
| History and Alerts | Retention picker, default 90 days. Current choices include 7, 30, 60, 90, 180, 365 days. Explain that shorter retention removes old records and longer retention cannot restore them. |
| Notifications | Popup Notifications and Alert on Icon switches; independent controls |
| Sounds | Notification Sounds switch; volume label/slider; Duck Sounds and Classic Chimes radio choices; two-column preview grid for Alarm, Snooze, Dismiss, Show Info |
| Quiet Hours | Enable switch in section header; two-handle overnight range; start/end labels and duration summary. Off dims/disables the range. Hour steps; start and end cannot match. |
| Default Alert Levels | Blue Info, yellow Warning, red Critical sliders with remaining-percent labels and ordering validation |
| Earned Reset Expiration Alerts | Reminder rows: severity picker, positive whole-number amount, Days/Hours picker, trash action; Add Reminder below |
| Quota Refresh Alerts | Same editor, separate schedule. Rows sort longest lead time first. |
| App Behavior | Keep Monitoring When Window Closes; launch at sign-in equivalent; Alert History; Send Test Notification |

First-use defaults from [RadarSettings](../src/AIResetRadar.Core/RadarSettings.cs): notifications, icon alerts, sounds, quiet hours, launch at sign-in, close-to-tray behavior, and version checks on; quiet hours midnight to 8 AM; volume 65%; Ducks; Custom Order; thresholds 50/30/10% remaining; history/alerts 90 days. Saved values in the live screenshots are not necessarily first-use defaults.

Prefer a useful info tooltip at the section header instead of repeated icons on every obvious control. Preserve individual help for controls whose meaning needs explanation. See [SettingsHelp](../src/AIResetRadar.App/Controls/SettingsHelp.cs) for the actual copy. Reminders share [ReminderEditor](../src/AIResetRadar.App/Controls/ReminderEditor.cs); settings share [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs).

## Account Settings

References: images 14-18 and 28-30.

- Heading identifies provider, nickname, and plan. Account Name edits the nickname, not provider identity.
- Line Color uses equal square swatches. Selected swatch has a white outline and check mark. The final matching square + opens Add Line Color. Choosing Add Color appends/selects the new color and leaves another + at the right. Preserve reusable custom colors. Image 16 shows the completed flow.
- Polling Interval is 1-30 whole minutes. The thumb snaps to actual vertical minute markers; rounding only the label is insufficient. Image 17 shows a dragged value at 10 minutes.
- Monitor This Account pauses automatic checks and alerts while retaining the connection. It is separate from the timeline filter and from per-quota alarms.
- Available Resets appears when the provider supplies a reserve. Show count and each known expiration; acknowledge partial details when applicable.
- Other Alerts contains Capacity Exhausted, Capacity Restored, and Reset Expiry Reminders. Each has its own explanation; omit a redundant top-level tooltip. Thresholds are edited on dashboard bars.
- Capacity Exhausted requires an enabled row alarm and a limit reaching zero remaining. Capacity Restored requires previously exhausted capacity becoming available with provider confirmation. Expiry reminders use the global expiration schedule and require at least one enabled row alarm for the account.
- Connection contains Sign In Again and Remove Account. Reconnect preserves settings and verifies identity/scope. Removal removes the connection from Scrooge, not the provider account. Demo disables these actions.

The compact polling tooltip in image 29 extends outside the captured app window; image 30 supplies its complete text. Treat this as a placement issue to avoid on Mac, not a required visual behavior.

## Connection and Detail Flows

References: images 13, 19-25, 35.

- Empty state retains the dashboard structure and offers Add Account and Explore with Demo Data. Demo is clearly labeled and its changes stay separate from real connections. The empty-state text currently omits Claude even though the chooser includes it; do not copy that omission as a product requirement.
- Add Account opens a three-provider choice dialog with Cancel. All providers use the same panel framing and nickname concept.
- Codex form: explanation, nickname, Sign In with OpenAI, Cancel. Installed CLI initiates browser sign-in. Pending sign-in has status text and a check-sign-in path; accept only verified account/usage results.
- Claude form: explanation including installed CLI requirement, nickname, Sign In with Claude, Cancel. The Windows Claude page currently disables its action without the shared busy ring; that is an implementation gap relative to the approved processing rule. Use consistent immediate processing feedback on Mac.
- GitHub browser form: nickname, AI Credits / Legacy Premium Requests unit, optional fallback allowance, advanced-mode switch, sign-in action, Cancel. Device-code instructions appear after starting authorization. Do not display or retain secrets in capture fixtures.
- GitHub advanced form additionally has a masked token field and optional organization billing owner. Browser sign-in remains the preferred visible path. The reference only opens the blank form; no credential is entered.
- Usage details include provider/account/plan, quota, used/remaining, reset time, account/scope, last successful fetch, provider-data time, and health. Live connections can offer Open Provider Usage; demo omits that action. The browser may be signed in to a different provider account, so retain the explanatory context.
- Available-reset details list reported count, snapshot time/health, and credit expirations. No redeem/claim action belongs in Scrooge.

Source: [MainPage.Actions](../src/AIResetRadar.App/MainPage.Actions.cs), [ConnectAccountPage](../src/AIResetRadar.App/Views/ConnectAccountPage.cs), [GitHub Form](../src/AIResetRadar.App/Views/ConnectAccountPage.GitHub.cs), [ConnectClaudePage](../src/AIResetRadar.App/Views/ConnectClaudePage.cs). Follow [Provider Integration](../docs/integration.md) and [Security](../SECURITY.md) for implementation, not just these visual summaries.

## Alerts and Desktop Lifecycle

References: images 08 and 32.

- In-app alerts sit at bottom right, with provider logo/name, severity-colored title and border, descriptive text, and Show Info / Snooze 1 Hour / Dismiss actions. Windows uses a maximum width of 520, padding 18, corners 14, and margin 20.
- Show Info reveals/highlights the matching account. Snooze persists the suppression before dismissing. Dismiss clears the current banner. A delayed action must not dismiss a newer banner.
- Alert History lists newest first and distinguishes delivered notifications from locally recorded entries. Entries open the associated account/window. Empty history has explanatory text.
- Send Test Notification creates a labeled test alert without changing quota values. Image 32 shows the in-app test banner; it is not a capture of the operating system notification center.
- Closing the main window can leave monitoring active. Explicit Exit/Quit stops it. Clicking the tray/menu-bar entry returns the dashboard. Preserve the distinction on macOS, with appropriate application-menu behavior.
- Windows tray provides Open, Refresh, and Exit plus alert status. Mac menu-bar/Dock status should communicate equivalent information, using native platform conventions.

Source: [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [MainPage.Desktop](../src/AIResetRadar.App/MainPage.Desktop.cs), [TrayService](../src/AIResetRadar.App/Platforms/Windows/TrayService.cs), [App Versions and Updates](../docs/app-updates.md).

## Remaining Capture and Verification Gaps

The gallery covers the main screens and representative states, not every possible runtime failure. These need source review and/or controlled Mac fixtures:

- Browser authorization, device-code challenge, sign-in success/failure/cancellation, reconnect identity mismatch, credential cleanup failure, and removal confirmation. No live accounts were connected or removed for this capture pass.
- Update downloading/progress, download failure, and restart handoff. Image 02 shows an available update; no update was installed.
- Native notification center and tray context menu; the in-app alert banner is captured.
- Offline/authentication/permission/rate-limit/unknown-allowance states, failed persistence, and empty alert history. Do not manufacture these against everyday accounts.
- Animated busy timing, keyboard-only operation, VoiceOver, long nicknames, very many accounts, DST geometry, and persistent state across a Mac restart. Still images cannot establish these.

## Mac Parity Acceptance Checklist

- [ ] Desktop and compact dashboard hierarchy, spacing, typography, and provider assets match.
- [ ] All four capacity colors, whole-number labels, unknown/stale states, and multiple windows per account match.
- [ ] Custom Order grip, sorting, row alarms, threshold dragging, and details remain independent interactions.
- [ ] History on/off, service filtering, day anchors, navigator, event details, sample readouts, and wheel routing match.
- [ ] Normal, hover, selected, processing, disabled, focus, and failure states are implemented consistently.
- [ ] All global/account settings, defaults, validation, autosave, color palette, and minute snapping match.
- [ ] Provider forms, cancellation, safe errors, and verified identity acceptance follow the integration contracts.
- [ ] Alert banner/history, snooze, return-to-account, background monitoring, and explicit Quit match their intended outcomes.
- [ ] Mac-native updates, credential protection, menu-bar integration, and notifications are reviewed separately from visual parity.
- [ ] Every intentional platform difference and remaining gap has a linked Mac screenshot or test result.
