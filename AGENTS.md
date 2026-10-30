## UI Copy
- Use Title Case for short UI titles, labels, tooltips, and concise status messages, capitalizing the principal words (for example, “Report Accounts”).
- Do not add a trailing period to short UI strings. Keep full explanatory sentences in sentence case with normal punctuation.

## Running App
- After each UI or runtime behavior update, rebuild and restart the running Windows app from the latest build, then confirm the visible app reflects the change before calling it complete. A successful build alone does not update an already-running instance.

## Reset Horizon Geometry
- Consecutive calendar-day markers must always be equally spaced, including Today and the first/last visible days. Compact all day widths together as the range grows; never reposition or clamp one day independently.
- Day labels, timeline dots, and grouped service icons must share the same day anchor. Preserve spacing during scrolling, resizing, history toggles, and daylight-saving transitions. Cover rendered day-anchor geometry with regression tests.
- Historical event markers and usage samples must share the actual timestamp scale. Center day headings in calendar-day buckets; never offset them by the current time of day or average nearby event timestamps.
