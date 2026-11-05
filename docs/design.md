# Design Guidelines

AI Token Scrooge helps people decide which account has capacity and when to use it. Every provider must feel like part of the same Windows app.

This is the current contributor design specification. It supersedes conflicting early concepts in [Design Selection](../design/selection.md). Use the running app and the source references below together: screenshots illustrate the appearance, while source controls define behavior. If the checkout differs from an approved rule here, identify the gap instead of introducing another competing pattern.

## Brand and Visual References

- Product name: AI Token Scrooge
- Tagline: Never Waste a Token! Never Miss a Reset!
- Mark: [Token Baron](../src/AIResetRadar.App/Resources/Images/token_baron.png)
- References: [Dashboard](images/dashboard.png), [Account Rows](images/accounts.png), [Settings](images/settings.png)
- Mac port comparison: [Windows UI Reference](../conversion-for-mac/mac-ui-parity.md) and [November 4 Screenshot Gallery](../conversion-for-mac/mac-ui-gallery.md), including desktop/compact states and the local changes newer than the published build

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

## Color Roles and Exact Values

Use **`#483A64`** for the selected/hover/processing purple. This is the left-hand Custom Order fill approved in the November 3 review. `#48306F` is retired and must not be introduced again. Reuse [AppPalette.InteractionPurple](../src/AIResetRadar.App/Controls/AppPalette.cs) in code; selected sort segments, alert segments, the navigator, and ActionButton already share it.

| State | Fill | Text and Icon | Behavior |
| --- | --- | --- | --- |
| Normal Action | `#232429` | Text `#F2F1F5`, icon `#B5A0F4` | Clickable |
| Hover or Pressed Action | `#483A64` | `#FFFFFF` | Clickable |
| Processing Action | `#483A64` | `#FFFFFF` | Animated ring, repeat clicks disabled |
| Selected Sort Segment | `#483A64` | Resting text/icon; white while hovered | Selection remains after pointer exit |
| Selected Alert Segment | `#483A64` | Existing lavender asset | Indicates the saved alert choice |
| Selected Navigator Range | `#483A64` | Existing timeline markers | Indicates visible dates |

Other purples are intentional **role-specific colors**, not alternative button fills. Light lavender is for legible foreground accents, darker tinted surfaces distinguish timeline events and badges, and native switches/sliders retain their listed states. Do not substitute a visually similar hex value. Account-line colors identify accounts; severity colors describe capacity. User-added line colors are allowed and are not theme tokens. Provider logos and bundled illustrations retain their original artwork colors.

### Complete Authored UI Palette

The table inventories explicit six-digit colors in App C# and XAML, including native controls. Sources are linked so a contributor can verify the intended role. Windows/system-provided colors and raster-art pixels are not authored UI palette values. Existing component-specific shades are documented here rather than silently recolored as part of the button correction.

| Color | Role | Source Controls |
| --- | --- | --- |
| `#141519` | Canvas, settings background, dark text on light swatches | [App](../src/AIResetRadar.App/App.xaml), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [MainPage](../src/AIResetRadar.App/MainPage.cs), [ConnectAccountPage](../src/AIResetRadar.App/Views/ConnectAccountPage.cs), [ConnectClaudePage](../src/AIResetRadar.App/Views/ConnectClaudePage.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#1B1C20` | Dashboard gradient start | [MainPage](../src/AIResetRadar.App/MainPage.cs) |
| `#1C1E24` | Cards and timeline day interiors | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [MainPage](../src/AIResetRadar.App/MainPage.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#202229` | History sample label background | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#208455` | Normal capacity gradient start | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#222329` | Dialogs, tooltips, menus, flyouts | [App](../src/AIResetRadar.App/App.xaml), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml) |
| `#232429` | Resting actions and navigator track | [App](../src/AIResetRadar.App/App.xaml), [AccountSortControl](../src/AIResetRadar.App/Controls/AccountSortControl.cs), [ActionButton](../src/AIResetRadar.App/Controls/ActionButton.cs), [CapacityAlertToggle](../src/AIResetRadar.App/Controls/CapacityAlertToggle.cs), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [ReminderEditor](../src/AIResetRadar.App/Controls/ReminderEditor.cs), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#2367B5` | Informational capacity gradient start | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#272331` | Account timeline selector background | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#2A2B30` | Editable text fields | [App](../src/AIResetRadar.App/App.xaml), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs) |
| `#2C293E` | Account plan badge background | [AccountQuotaView](../src/AIResetRadar.App/Controls/AccountQuotaView.cs) |
| `#30333C` | History day grid lines | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#30333E` | Unused capacity track | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs), [ThresholdEditor](../src/AIResetRadar.App/Platforms/Windows/ThresholdEditor.cs) |
| `#343741` | History percentage grid lines | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#352A50` | Quota refresh event tile background | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#363842` | Dashboard header divider | [MainPage](../src/AIResetRadar.App/MainPage.cs) |
| `#383B46` | Card outlines and section dividers | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [MainPage](../src/AIResetRadar.App/MainPage.cs), [MainPage.Ordering](../src/AIResetRadar.App/MainPage.Ordering.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#3B3D47` | Dialog separators and global Line resource | [App](../src/AIResetRadar.App/App.xaml), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml) |
| `#41434F` | Unfilled slider tracks | [QuietHoursRange](../src/AIResetRadar.App/Controls/QuietHoursRange.cs), [MinuteEditor](../src/AIResetRadar.App/Platforms/Windows/MinuteEditor.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#45464D` | Action outlines and segment separators | [App](../src/AIResetRadar.App/App.xaml), [AccountSortControl](../src/AIResetRadar.App/Controls/AccountSortControl.cs), [ActionButton](../src/AIResetRadar.App/Controls/ActionButton.cs), [CapacityAlertToggle](../src/AIResetRadar.App/Controls/CapacityAlertToggle.cs), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs) |
| `#483A64` | Shared selected, hover, processing, alert selection, navigator fill | [AppPalette](../src/AIResetRadar.App/Controls/AppPalette.cs) |
| `#49321D` | Earned-reset expiry tile background | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#4DCE8A` | Normal capacity gradient end | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#51476D` | Account plan badge border | [AccountQuotaView](../src/AIResetRadar.App/Controls/AccountQuotaView.cs) |
| `#515461` | Usage track outline | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#55C9BD` | Teal account-line swatch | [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#58A6FF` | Informational severity, blue account-line swatch | [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [ReminderEditor](../src/AIResetRadar.App/Controls/ReminderEditor.cs), [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [ThresholdEditor](../src/AIResetRadar.App/Platforms/Windows/ThresholdEditor.cs), [HistoryChartLayout](../src/AIResetRadar.App/Presentation/HistoryChartLayout.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#6553B8` | Native accent pressed state | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml) |
| `#65577F` | Dialog, popup, palette-add outlines | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#7160B4` | Quiet-hours thumb outline | [QuietHoursRange](../src/AIResetRadar.App/Controls/QuietHoursRange.cs) |
| `#7560CE` | Native accent default state | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml) |
| `#8170B7` | Timeline rail | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#8170CF` | Settings switch fill and pressed radio state | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#8670CC` | History switch fill | [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs) |
| `#8973DB` | Native accent secondary state | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml) |
| `#919195` | Unchecked timeline account label | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#A5A8BA` | Tertiary supporting text | [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#A990ED` | Filled slider tracks and selected radio fill | [QuietHoursRange](../src/AIResetRadar.App/Controls/QuietHoursRange.cs), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#AE8519` | Warning capacity gradient start | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#B197F2` | Settings scrollbar thumb | [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#B52D42` | Critical capacity gradient start | [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs) |
| `#B5A0F4` | Standard accent icons, timeline refresh markers and day headings | [App](../src/AIResetRadar.App/App.xaml), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [SettingsHelp](../src/AIResetRadar.App/Controls/SettingsHelp.cs), [TimelineDayHeading](../src/AIResetRadar.App/Controls/TimelineDayHeading.cs), [MainPage.Ordering](../src/AIResetRadar.App/MainPage.Ordering.cs), [AccountDragController](../src/AIResetRadar.App/Platforms/Windows/AccountDragController.cs), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs), [ConnectAccountPage.GitHub](../src/AIResetRadar.App/Views/ConnectAccountPage.GitHub.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#B6B8C8` | Secondary text and placeholders | [App](../src/AIResetRadar.App/App.xaml), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [MainPage](../src/AIResetRadar.App/MainPage.cs), [ConnectAccountPage](../src/AIResetRadar.App/Views/ConnectAccountPage.cs), [ConnectClaudePage](../src/AIResetRadar.App/Views/ConnectClaudePage.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#B99AF8` | Default lavender account line and historical refresh marks | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HistoryChartLayout](../src/AIResetRadar.App/Presentation/HistoryChartLayout.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#BDA3F5` | Quota countdown accent | [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs) |
| `#BEC1D2` | Timeline dates and supporting details | [AccountSortControl](../src/AIResetRadar.App/Controls/AccountSortControl.cs), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [TimelineDayHeading](../src/AIResetRadar.App/Controls/TimelineDayHeading.cs), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#C3ADF8` | Section/brand highlights, badge text, slider thumbs | [AccountQuotaView](../src/AIResetRadar.App/Controls/AccountQuotaView.cs), [QuietHoursRange](../src/AIResetRadar.App/Controls/QuietHoursRange.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [MainPage](../src/AIResetRadar.App/MainPage.cs), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [MinuteEditor](../src/AIResetRadar.App/Platforms/Windows/MinuteEditor.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#CDEB82` | Lime account-line swatch | [HistoryChartLayout](../src/AIResetRadar.App/Presentation/HistoryChartLayout.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#D1BEFF` | Today emphasis and navigator handle | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [TimelineDayHeading](../src/AIResetRadar.App/Controls/TimelineDayHeading.cs) |
| `#D587A5` | Pink account-line swatch | [HistoryChartLayout](../src/AIResetRadar.App/Presentation/HistoryChartLayout.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#E5DCF9` | Navigator Today line | [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs) |
| `#EAAA51` | Earned-reset expiry, attention messages; not capacity-warning yellow | [App](../src/AIResetRadar.App/App.xaml), [HistoryTimelineView](../src/AIResetRadar.App/Controls/HistoryTimelineView.cs), [HorizonView](../src/AIResetRadar.App/Controls/HorizonView.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#F05B69` | Critical capacity and red account-line swatch | [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [ReminderEditor](../src/AIResetRadar.App/Controls/ReminderEditor.cs), [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#F2C94C` | Warning capacity and yellow account-line swatch | [QuotaRowView](../src/AIResetRadar.App/Controls/QuotaRowView.cs), [ReminderEditor](../src/AIResetRadar.App/Controls/ReminderEditor.cs), [UsageBar](../src/AIResetRadar.App/Controls/UsageBar.cs), [MainPage.Alerts](../src/AIResetRadar.App/MainPage.Alerts.cs), [ThresholdEditor](../src/AIResetRadar.App/Platforms/Windows/ThresholdEditor.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#F2F1F5` | Primary resting text | [App](../src/AIResetRadar.App/App.xaml), [AccountSortControl](../src/AIResetRadar.App/Controls/AccountSortControl.cs), [ActionButton](../src/AIResetRadar.App/Controls/ActionButton.cs), [QuietHoursRange](../src/AIResetRadar.App/Controls/QuietHoursRange.cs), [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs), [SettingsPage](../src/AIResetRadar.App/Views/SettingsPage.cs) |
| `#FF5D68` | Native critical threshold handle | [ThresholdEditor](../src/AIResetRadar.App/Platforms/Windows/ThresholdEditor.cs) |
| `#FFFFFF` | Hover/processing text and icons, check marks, focus contrast | [App](../src/AIResetRadar.App/Platforms/Windows/App.xaml), [ThemedDialogs](../src/AIResetRadar.App/Services/ThemedDialogs.cs) |

### Transparency and Derived Colors

- Transparent (`#00000000`) is reserved for input overlays and unfilled regions, not visible action surfaces.
- The modal scrim is black at alpha 160/255 (`#A0000000`); it dims the dashboard behind a panel.
- History area fills use the selected account color at 9% opacity. Historical event lines and sample guides use 60%; unchecked selector outlines use 50%.
- White (`#FFFFFF`) also appears through named platform colors for check marks and sample outlines. It is the foreground for hovered and processing actions.
- Capacity gradients run from `#208455` to `#4DCE8A` (normal), `#2367B5` to `#58A6FF` (info), `#AE8519` to `#F2C94C` (warning), and `#B52D42` to `#F05B69` (critical).

### Maintaining the Palette

Before adding a color, choose an existing role above. Shared interaction fills must use AppPalette; use existing shared controls for other roles. If a new semantic role needs a new color, add its exact value, purpose, source, and states to this table in the same change. Keep native XAML resources synchronized where a role is duplicated. Check normal, hover, selected, processing, disabled, and pointer-exit behavior in the running app. Do not use a muted disabled style for an operation still processing.

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

ActionButton uses a 44-unit height, 10-unit corner radius, and a lavender icon to the left of its text. Action buttons use the shared selected purple (`#483A64`) with white text and icons on hover. Processing keeps exactly those colors, replaces the icon with an animated progress ring, and disables repeat clicks until completion or failure. Use an action-specific label such as Checking or Refreshing with an ellipsis; restore the original label afterward. Reuse ActionButton.BeginBusyAsync or RunAsync rather than adding separate spinners. Set the busy state synchronously on activation, then yield at low dispatcher priority before sound playback or other work so Windows can paint the feedback first. Never play a sound or initialize a provider before showing the busy state. Settings cards use 15-unit corners. Usage bars use a 48-unit height and 11-unit corners. Keep icon-only actions accessible by name. Use existing Fluent/Lucide icon treatments and record licenses for new assets.

When restoring from the tray, show the existing dashboard first. Schedule the refresh after its first rendered frame and keep provider checks off the UI thread.

## Account Cards and Capacity

Group all quota windows for one account in one card. Keep provider identity, nickname, and plan together. Each window gets its own bar, alert controls, reset countdown, and exact reset date. Do not combine session and weekly allowances or duplicate Used/Left values in an extra column.

Bar length represents used capacity. Keep both Used and Left labels readable over the entire track at 0%, 100%, and overage. Display dashboard percentages as whole numbers; retain underlying precision for arithmetic. Unknown usage is not 0% usage. Stale data must not appear as a newly confirmed healthy reading.

Custom Order uses the selected six-dot grip treatment. Preserve the user's ordering, filtering, and selected controls during background refreshes. A new provider should use the existing card instead of introducing a provider-specific dashboard layout.

## Reset Horizon Invariants

- Wheel input over the chart, day headings, and event icons pans the timeline horizontally and never scrolls the dashboard vertically, including at timeline boundaries. Attach wheel handling to the common chart host so overlaid clickable event tiles follow the same route. Keep event clicks, tooltips, and keyboard navigation intact.

- History on/off uses one timeline renderer for day headings, rail, circles, future events, and navigator. Toggle only the historical content.
- Keep service selectors visible. An unchecked account filters its history and future events. With History off, users cannot navigate before Today.
- Consecutive calendar-day anchors are equally spaced, including Today and both endpoints. Compact widths together; never move one label or day independently.
- Day labels, dots, and grouped service icons share the same day anchor. Preserve alignment during scrolling, resizing, filtering, toggling, and DST transitions.
- Center day headings in calendar-day buckets. Place historical samples and events on their actual timestamp scale; do not shift them by the current time or averaged timestamps.
- Upcoming event tooltips show the account and event, then the same large lavender countdown and smaller local date and time used beside a quota bar. Calculate the countdown when the tooltip opens.
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

The November 3 button-state check used the existing dashboard and Settings action buttons as its reference. Native desktop and 800-by-700 checks showed purple processing states for Refresh and Check Now, a white animated ring, and the normal/hover appearance returning after completion. The local build and 425 regression tests passed. Sign-in, download completion, and restart were not exercised as part of this visual check.

Before editing, identify the closest production control and name it in the PR. After editing, rebuild and restart the Windows app from the new output. Compare the actual app at a representative desktop size and the 800-by-700 compact preview, including Windows scaling. Check navigation, keyboard focus, empty/error states, and long content. Include redacted screenshots and state any unverified cases. Compilation alone does not establish visual correctness.
