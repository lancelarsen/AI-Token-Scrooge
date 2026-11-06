# Provider Integration Guide

We welcome contributions for additional services, including Grok. Scrooge monitors account usage; adding a model name or an inference API does not by itself provide a usable quota integration. Begin by proving how that service exposes the user's allowance, consumption, identity, and refresh times.

Read [Architecture](architecture.md), [Design Guidelines](design.md), and [Testing](testing.md) before implementation.

## Integration Principles

Prefer a simple Sign In action that hands authentication to the provider's browser flow or installed official CLI. Users should not need to extract browser cookies or copy hidden tokens. Keep advanced token entry optional when it is genuinely necessary and permission requirements can be explained.

A connection must read usage without sending a model prompt, redeeming a reset, buying credits, or switching the user's account in another app. An API key that permits inference is not evidence that the provider exposes subscription usage. Subscription allowances and API billing may be different products.

Prefer a documented, supported usage endpoint. If an internal endpoint is necessary, isolate it, document its role and failure behavior, and avoid making its optional fields a prerequisite for unrelated working reads. Do not bypass access controls or silently scrape shared browser sessions.

Follow [Credential Storage Policy](../SECURITY.md#credential-storage-policy): keep Codex's explicit keyring mode, GitHub's current-user DPAPI store, and Claude's supported CLI-managed file with verified Windows permissions. Do not equate isolation with encryption or replace a CLI's credential format without compatibility evidence. The [Storage Decisions](decisions/credential-storage.md) retain Claude's file format and record the GitHub PKCE compatibility blocker; neither additional Claude encryption nor PKCE is implemented. Track remaining lifecycle and migration work in the [Credential Protection Plan](../tasks/credential-protection-plan.md).

## Existing Implementations

New CLI sign-ins use the local Security root supplied by AppServices. Preserve ProfileLocation on the account, pending sign-in, and retired credential. Old records default to their legacy data-directory location. Never move a Codex profile or change its location flag without validating the corresponding keyring lookup. Acceptance must match the journal's provider and location, and cleanup must use the retired credential's location rather than the replacement account's.

| Provider | Sign-In and Identity | Usage Source | Important Boundaries |
| --- | --- | --- | --- |
| OpenAI Codex | Installed Codex executable, browser sign-in, isolated CODEX_HOME; identity from account/read | account/rateLimits/read through app-server stdio | Multiple reported windows and optional earned resets; no invented missing window |
| GitHub Copilot | GitHub App device flow; numeric identity from /user; optional advanced token path | REST personal/organization billing usage; copilot_internal/user for personal allowance lookup | Distinguish credits from requests, personal from organization billing, official report from internal entitlement lookup |
| Claude | Installed Claude CLI auth login and auth status, isolated CLAUDE_CONFIG_DIR | /usage command, version gate 2.1.310 in current implementation | Subscription identity and organization checked on refresh; session/weekly parser; missing or unreadable fields remain unavailable |

These are descriptions of this repository's implementation, not guarantees that provider interfaces never change. Verify the source and supported CLI/API versions when contributing.

### Devin Enterprise

The Windows app opens the tenant's Devin site in a dedicated WebView2 profile. Workspace input accepts an HTTPS tenant on `devinenterprise.com` and discards any path, query, or login hint. The user completes company sign-in in the provider page. Scrooge reads the user ID from Preferences before and after reading My Analytics and binds the account to the enterprise host. Personal totals span the user's organizations within that enterprise.

My Analytics supplies monthly Agent Compute Units (ACUs). The adapter prefers the usage-limit meter. When only the consumption figure is visible, it leaves the allowance unknown. It accepts a reset only when the displayed date and time resolve unambiguously in the browser's time zone within the next monthly window. Background refresh uses the same account profile and never opens a sign-in prompt. The provider controls the page layout and role permissions. See [Devin Personal Analytics](https://docs.devin.ai/enterprise/security-access/personal-analytics).

On November 6, 2026, one Windows user connected through enterprise SSO in the rebuilt app. Scrooge verified the stable user ID, matched the allowance, usage, and reset shown in My Analytics, and read all three again after an app restart without prompting for sign-in. Other enterprises, IdPs, languages, and expired live SSO sessions need separate verification.

### Codex

[CodexConnection](../src/AIResetRadar.Infrastructure/CodexConnection.cs) launches app-server with argument-list construction, isolated profile storage, and an explicit method allowlist. It correlates requests and responses and closes pending work on termination. Do not widen the allowlist to inference or redemption operations.

[AccountMonitor](../src/AIResetRadar.Infrastructure/AccountMonitor.cs) completes browser sign-in, reads identity and usage, and checks identity again during refresh. [ProviderMapping](../src/AIResetRadar.Infrastructure/ProviderMapping.cs) handles rate-limit data and earned resets. The provider may return only one window or reserve information without ordinary windows.

### GitHub

[GitHubDeviceFlow](../src/AIResetRadar.Infrastructure/GitHubDeviceFlow.cs) implements device authorization with approval in the browser, not the authorization-code/PKCE flow. [GitHubCredentialManager](../src/AIResetRadar.Infrastructure/GitHubCredentialManager.cs) handles saved credentials and renewal. [GitHub Sign-In Setup](github-sign-in.md) documents app registration; public client IDs are not client secrets.

[GitHubUsageClient](../src/AIResetRadar.Infrastructure/GitHubUsageClient.cs) resolves /user on reads, requests the correct billing scope and unit, bounds response size, and classifies auth, permission, transient, and rate-limit failures. Personal allowance lookup uses an internal endpoint. An optional lookup failure must not turn unavailable allowance into a guessed denominator. Manual allowance has explicit provenance.

### Claude

[ClaudeClient](../src/AIResetRadar.Infrastructure/ClaudeClient.cs) removes inherited ANTHROPIC_/CLAUDE_ environment overrides and assigns its own profile directory. It validates claude.ai identity, subscription context, and organization; rejects identity changes; and terminates the process tree on cancellation. It checks the CLI version before calling /usage. Do not replace an unsupported usage command with a prompt asking Claude to estimate usage.

The current parser expects particular English section labels and date formats. Treat locale and output-format changes as explicit test cases and compatibility work, not silent zero usage. Reset timestamps can be unavailable or imprecise; preserve that uncertainty.

## Before Writing an Adapter

Start from the [Provider Research Template](providers/provider-template.md) and create a provider-specific note under `docs/providers/` with:

- The exact product being monitored: consumer subscription, coding assistant, team allowance, or API billing
- Authoritative source links, date checked, CLI/API version, account types tested, and permissions required
- The browser/CLI sign-in sequence, identity fields, scope, credential ownership, renewal, logout, and cancellation behavior
- Sample redacted fields for usage units, allowance provenance, reset boundaries, provider-data timestamp, and earned credits if supported
- What is unknown, delayed, internal, version-sensitive, unsupported, or manually supplied
- A read-only comparison with the provider's own usage screen and the remaining live checks

Use synthetic fixtures for tests. Never commit raw responses or personal identifiers. Do not mark a candidate supported until usable data and account isolation have been demonstrated. If the data source is unavailable, retain a research task rather than adding a misleading working-provider button.

## Implementation Map

1. Add provider transport and mapping in Infrastructure. Keep parsing independently testable; use HttpClient/TimeProvider injection where appropriate, bounded I/O, cancellation, and safe user-facing errors.
2. Normalize into ProviderAccount and QuotaSnapshot. Use stable window IDs, explicit units, nullable unknowns, and Provider/Manual/Unknown allowance provenance. Preserve raw overage and partial reset details.
3. Add explicit connect, refresh, and disconnect routes in AccountMonitor. Verify Subject and Scope before accepting every refresh. Reconnect must preserve logical account identity and settings while validating fresh credentials.
4. Extend the existing connection views (`ConnectAccountPage`, its GitHub partial, or the focused `ConnectClaudePage` pattern). Use shared controls and provider-owned browser sign-in. Handle cancel, denied authorization, missing CLI, and retry.
5. Add presentation identity through existing provider icon, plan-label, link, and account-row paths. Search existing provider literals across App/Core/Infrastructure to find all dispatch points; do not implement a second timeline or provider-specific usage bar.
6. Integrate polling, history, alerts, disabled accounts, and cleanup through existing orchestration. A late response from an old credential must not be applied after reconnect/removal.
   Stage the new provider/credential ID through ICredentialLifecycleJournal before sign-in can persist credentials. Account acceptance clears it transactionally; startup retries abandoned sign-ins through the retirement queue. Complete cleanup only after secret removal or CLI logout and validated profile deletion succeed. Add the provider to journal validation and explicit dispatch routes.
7. Add tests, sanitized demo data, contributor evidence, and user-facing prerequisites. Update the supported-provider list only to the extent the integration is actually verified.

## Required Verification

### Credential Checklist

- [ ] Document the provider-supported storage mechanism, Windows protection, credential permissions, and the exact CLI/API versions checked. Distinguish encryption from file permissions.
- [ ] Keep secrets separate from SQLite, logs, command arguments, URLs, screenshots, fixtures, and release artifacts. Use synthetic sentinels to verify these boundaries.
- [ ] Use the local Security root for new profiles and preserve explicit location references through sign-in, acceptance, reconnect, and retirement. Validate ownership and reject redirected paths before writing or deleting.
- [ ] Journal sign-in before credentials can be created. Commit the accepted account before retiring its predecessor; retain retry state after failed cleanup.
- [ ] Serialize credential renewal and removal across all possible writers. Test interruption after remote rotation and before local save, and prevent stale retries from restoring a removed or replaced credential.
- [ ] Verify identity, scope, two-account isolation, cancellation, restart, renewal, and logout. Keep synthetic, native, and live results separate; identify missing test accounts or Windows users.
- [ ] Bound CLI output and execution time, filter inherited authentication overrides, and use the expected installed executable. Verify that monitoring never invokes inference.
- [ ] Document local cleanup separately from remote revocation, including any impact on shared tokens or other sessions. Add provider-specific guidance to [Credential Recovery](credential-recovery.md) and identify unverified revocation behavior.

### Behavior Matrix

| Area | Cases |
| --- | --- |
| Mapping | Normal, zero, exhausted, overage, missing allowance, missing reset, multiple windows, malformed payload, partial reserves |
| Identity | Two accounts, changed identity, wrong scope, rejected reconnect, same-account reconnect |
| Authentication | Cancellation, denied approval, renewal, revoked credentials, missing or incompatible CLI |
| Transport | Timeout, offline, 401/403, 429/retry deadline, oversized or malformed output, process exit |
| Lifecycle | Disabled account, removal during polling, late response, failed persistence, retirement cleanup retry |
| UI | Shared card, unknown/stale state, whole-number percentage labels, events, filtering, long account names, compact layout |
| Live Evidence | Read-only reconciliation, restart, sign-out isolation, and an honest list of untested account types |

Polling tests must not send inference requests. Verify no secrets appear in logs, persisted non-secret state, release packages, or screenshots. See [Security](../SECURITY.md).

## Candidate Providers

Track Grok/xAI, Google Gemini, Cursor, Windsurf, and other community proposals in the [Contribution Roadmap](../tasks/contributor-roadmap.md). Their inclusion is an invitation to investigate; it does not assert that each has an accessible subscription-usage API. Propose API-billing monitoring separately when it would introduce different units and user expectations.
