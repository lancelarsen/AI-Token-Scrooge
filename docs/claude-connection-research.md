# Claude connection feasibility — October 26, 2026

Requested: add Claude browser login and usage monitoring for a Free account, using the user's existing Chrome sign-in.

## Live findings

Opened Claude's official login URL in Chrome. The existing session redirected to the signed-in app. The account-menu identity matched the user-provided account, and the plan was Free. No password, cookie, session token, or chat content was extracted.

The Settings navigation had no Usage entry. Searching Settings for `usage` returned `No matching settings`. This establishes the visible limitation for this tested Free account; it does not prove that every possible undocumented endpoint lacks data.

## Official documentation

- [Usage limit best practices](https://support.claude.com/en/articles/9797557-usage-limit-best-practices): the documented Settings > Usage meters apply to Pro, Max, Team, and seat-based Enterprise plans.
- [Log in to your Claude account](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account): browser login supports Google or emailed login links; third-party product integration is directed toward Claude Console API authentication. An API billing connection would not establish this Free subscription's remaining allowance.

No supported Free-subscription allowance-reading integration was established in this investigation. Browser sign-in alone is not a Scrooge provider credential or evidence of automatic monitoring. No Claude Code client identity, existing external credentials, or browser cookies were borrowed for Scrooge.

## Pro and official-client integration

The user upgraded to Pro and explicitly chose the same official-client approach as OpenAI, rejecting a browser extension. The browser Usage page showed current-session and weekly meters at 0% used. The current session initially had no start; the weekly reset was Wednesday at midnight. No reset credits were available.

The installed Claude Code 2.1.71 successfully authenticated through its own browser flow in a Scrooge-owned isolated profile. Its interactive `/usage` returned both limits without a model prompt. During this check the client's own updater installed 2.1.310. That version was verified to support `claude /usage` with redirected output and immediate exit, returning session/weekly percentages and reset dates with America/Chicago explicitly named. Scrooge uses that simpler command, requires 2.1.310 or later, and never falls back to a model prompt. The exploratory terminal renderer was removed.

`claude auth login` and `claude auth status` establish the account identity and plan. Each connection has its own GUID directory under Scrooge's ClaudeProfiles folder using the documented CLAUDE_CONFIG_DIR setting. No external Claude profile, browser cookie, OAuth client implementation, or subscription token is copied into Scrooge. Credentials remain managed by the official client. The earlier connection-probe profile is a temporary investigation profile, not a saved dashboard account.

The reader checks the account email and organization before refreshing. It parses only recognized usage sections and percentages; unsupported output fails visibly. Reset dates are minute-precision, resolved in the supplied timezone. Unknown, ambiguous, or implausible dates remain unavailable. Reset reserves and absolute token allowances are not supplied by this source and are not invented.

References: [Claude Code authentication and multiple accounts](https://code.claude.com/docs/en/iam), [CLI commands](https://code.claude.com/docs/en/cli-reference), [official usage command reference](https://support.claude.com/en/articles/14553413-claude-code-cheatsheet). The redirected-output behavior is a live observation of 2.1.310, not a guarantee of a stable machine-readable API; future output changes may require an adapter update.

## Verification status

- 155 solution tests passed (59 Core, 96 Infrastructure), including malformed percentages, missing windows, timezone/reset parsing, and current CLI text fixtures.
- Windows publish succeeded for `artifacts/publish/claude-win-x64`. The running native app opened the new Claude chooser and sign-in page. The form reuses the existing OpenAI/GitHub connection form spacing, dark palette, and ActionButton design.
- The updated official client requested additional file-upload and plugin scopes at its authorization page. Per-action browser approval was requested; the user then completed authorization themselves and confirmed login.
- On October 26 at 4:08 PM America/Chicago, a restarted native app loaded and refreshed Claude Personal / Pro alongside the existing OpenAI and GitHub accounts. Both Claude meters were 0% used / 100% left, with session reset 8:30 PM and weekly reset Wednesday October 28 at midnight. Both events appeared in Reset Horizon. Native screenshots confirmed the grouped card at desktop and narrow widths. Full refresh-token expiry and a second Claude account have not been tested.
