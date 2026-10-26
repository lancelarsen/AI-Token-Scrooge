# AI Reset Radar — selected design direction

## Confirmed base

The user selected the original **Reset Horizon**, image 3 from the first exploration, by attaching the exact screenshot. Its charcoal/lavender design, brand header, upcoming reset timeline, account usage rows, earned-reset summary and popup are the starting point.

Selected reference: `D:/Dev/AI/AIResetRadar/design/references/reset-horizon-selected.png`.

## Confirmed feedback

- Remove the redundant Used/Left numeric column between the usage bar and reset information.
- Make usage bars taller and use the recovered horizontal space to widen them.
- Put both Used and Left labels and values **inside** each bar; do not repeat them above/below the bar or in another column.
- Keep quota reset information distinct and readable.
- The user reviewed eight refinements and selected the attached amber-warning design as the final visual target.

## Final visual target — selected

On October 25, 2026, the user attached the desired screenshot and said “let's go with this one.” It visually matches **round 2, image 7 — Quiet Warning**. The exact user-attached file is preserved as the authoritative target at `D:/Dev/AI/AIResetRadar/design/references/approved-ui.png`; use it for implementation comparisons rather than relying on option numbering or the temporary clipboard path.

![Approved UI](D:/Dev/AI/AIResetRadar/design/references/approved-ui.png)

Preserve these selected details:

- Dark charcoal surfaces, lavender branding and normal-state usage bars.
- Horizontal chronological reset timeline, with an amber clock for earned-reset expiry.
- Four separate rounded quota rows in the reference, each showing account identity, a wide tall usage bar, and a separate reset-time block.
- Both Used and Left values inside each bar; no duplicate numeric column.
- Amber warning fills, narrow amber left edges and warning icons for low-capacity rows. Thresholds remain configurable as specified in the plan.
- Available-reset summary in the upper right and compact notification popup at lower right, with View account and Snooze actions.

The visual selection is complete. Proceed through the existing plan when the user starts implementation or invokes the prepared `/goal`; this selection pass updates planning artifacts only.

All bars in these studies use filled length for Used, with Left in the remaining track. For production, keep text readable even near 0% or 100% by placing labels over the entire track with appropriate contrast rather than forcing text to fit tiny segments. Follow `tasks/plan.md` for exact arithmetic and source timestamps; generated pixels are visual references.

## Later user-approved refinements (October 25-26, 2026)

- Product name: AI Token Scrooge; catchphrase: Never Waste a Token!
- Group each account's quota windows in one card, with compact stacked bars and plan identification.
- Integrated Reset Horizon panel with history-clock icon, inline legend, divider, daily ticks and provider/plan event icons. Remove the right-side reset-count button and expiry caption; tighten and balance horizontal endpoint margins. Refined visual reference: `timeline-header-concepts/04-refined-selected-layout.png`.
- Keep reset details accessible through event clicks and account settings, including balances whose expiry is unavailable.
- Account Settings buttons open only the selected account's controls.
- Top bar: update time/date to the left of Refresh/Settings, vertically aligned brand block with larger catchphrase.
- Settings: rounded charcoal cards and draggable two-handle purple quiet-hours range; matching capacity sliders.
- App action buttons: same icon-plus-text treatment as Refresh/Settings; consistent rounded border, charcoal fill and lavender icon.
- Selected logo: concept 01, The Token Baron (October 26). Transparent production mark: `src/AIResetRadar.App/Resources/Images/token_baron.png`; multi-resolution Windows icon: `src/AIResetRadar.App/Resources/app.ico`. Original concept remains in `logo-concepts/01-token-baron.png`.
