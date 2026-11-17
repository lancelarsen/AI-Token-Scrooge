# OpenAI API Credit Pool

Scrooge's API credit work tracks the full API organization's dollar balance, including purchased and promotional credits. It is separate from Codex subscription windows and earned resets. The monthly subscription benefit amount is not the current credit balance.

## Status and Evidence

Status: Chrome Extension Preview Implemented With Fixtures; live extension connection and background refresh acceptance remain open. See [Chrome Extension Setup](../chrome-api-credits.md).

Research date: November 17, 2026. Repository baseline: `c799788`. The API dashboard has no published version identifier for the internal endpoints observed below.

A read-only check in an existing authorized Chrome session reconciled the billing overview, credit-grant table, and the dashboard's credit-grant response. Reloading the grant page returned the same balance. Only field names and sanitized findings belong in this document; live account identifiers, grant identifiers, amounts, and raw responses are omitted.

The original Windows connection reached provider sign-in in WebView2, but Google passkey authentication failed in Windows Hello. The screenshots did not establish the underlying passkey failure cause. At the user's request, the embedded adapter has been replaced by a Chrome extension and native messaging bridge using the existing browser login. Keep live acceptance open until extension installation, confirmation, refresh, restart, and cleanup have been exercised.

## Source and Billing Semantics

| Source | What It Establishes | Limit |
| --- | --- | --- |
| [API Credit Benefit Guide](https://developers.openai.com/api/docs/guides/chatgpt-api-credits) | Subscription credits fund a linked API organization | Benefit terms do not establish delivery or remaining balance |
| [Benefit Help](https://help.openai.com/en/articles/20001626-api-credits-benefit-for-eligible-chatgpt-subscriptions) | Grants can be prorated, expire, and do not roll over | A grant expiration does not guarantee a replacement grant |
| [Costs API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs) | Organization spending by reporting period | Costs are not the credit ledger or an available balance |
| [Prepaid Billing](https://help.openai.com/en/articles/8264644-setting-up-and-managing-prepaid-api-billing) | Promotional credits are used before purchased credits; billing can lag | Do not treat the balance as an instantaneous spending cutoff |
| Live Platform Billing Pages | Aggregate balance and per-grant amounts and expirations | Internal dashboard format and permissions can change |

The inspected grant records did not identify a subscription-benefit source. The agreed scope is therefore the full API credit pool. Scrooge must not label all promotional credits as subscription-benefit credits or subtract organization costs from an assumed monthly allowance.

## Authentication and Ownership

The existing Codex integration reads `account/rateLimits/read` through an isolated official CLI profile. That is a different connection from API Platform billing.

The observed billing request was `GET https://api.openai.com/v1/dashboard/billing/credit_grants`. It used a Platform session credential and an `OpenAI-Organization` header. The signed-in dashboard obtains user identity and organization membership from `https://api.openai.com/v1/dashboard/onboarding/login`. No public credit-balance API was found in the official references reviewed. Ordinary API-key access to the dashboard endpoint was not tested and must not be claimed.

The current adapter uses the [Chrome extension and native bridge](../chrome-api-credits.md). The user selects an organization in Chrome and confirms its normalized reading in the extension popup. A reload verifies identity and scope before acceptance. Background reads use temporary inactive tabs, reject a different user or organization, and never switch the browser account. The extension observes the page's own requests; it does not extract a token or perform inference, purchases, or benefit activation.

The native bridge accepts bounded, normalized messages from the pinned extension origin. Scrooge correlates each result to a live request over a current-user-only named pipe and validates identity, totals, and grants again. No HTTP listener or raw response persistence is used. The page-context observer is not an attestation mechanism: the Platform page and same-user local environment remain trust boundaries.

Connection IDs still use the lifecycle journal and `OpenAiBillingProfiles/<credential-id>` directory so removal can clean up old experimental profiles through the existing guarded path. New connections do not store browser credentials there. Removing a Scrooge account does not sign out Chrome. Google's [embedded-browser policy](https://developers.google.com/identity/protocols/oauth2/policies) is an additional reason to use the normal browser, but the observed Windows Hello error was not proof of a Google policy rejection.

The installed Codex client `0.167.0-alpha.3.2` was also inspected through its generated experimental app-server schema. Its account usage reader exposes token activity and thread usage estimates, not an API organization balance. The benefit-preview interface exposes benefit terms, not remaining credits. No supported API-billing OAuth callback equivalent to Codex login was found in the reviewed interfaces.

## Data Contract

| Field | Observed Source | Treatment |
| --- | --- | --- |
| User Identity | `user.id` in the login response | Require a stable identifier |
| Organization Membership | `user.orgs.data[].id` | Require membership in the request's billing organization |
| Billing Scope | `OpenAI-Organization` on the grant request | Bind the connection and verify every refresh |
| Granted USD | `total_granted` | Preserve decimal precision and provider provenance |
| Used USD | `total_used` | Preserve decimal precision |
| Available USD | `total_available` | Reconcile with granted minus used; preserve negative overage |
| Grant Remaining | `grant_amount - used_amount` | Keep positive, effective, available grants for expiry display |
| Grant Timing | `effective_at`, `expires_at` | Unix timestamps; keep unknown expiry unknown |
| Exclusions | `is_scheduled`, `is_held_for_prepaid_transition` | Do not show these as currently available grants |
| Provider Data Timestamp | Not supplied by the inspected response | Keep unknown; record the local observation time separately |
| Benefit Attribution | Not supplied by the inspected grant records | Do not infer it |

An unreadable or inconsistent response fails as unsupported instead of becoming zero usage. A zero pool and negative available balance are valid accounting states. Expirations are amber timeline events, not quota refreshes. The shared bar shows dollar amounts; history uses percentage remaining in the reported pool and starts a new segment when its denominator changes.

## Implementation and Acceptance

`OpenAiBilling` defines identity and grant semantics. `OpenAiBillingClient` owns connection, leases, identity checks, and retirement. `ChromeBillingBridge` owns request correlation and `ChromeBridgeProtocol` validates normalized readings. The extension owns billing tabs and explicit organization confirmation. The shared account cards, capacity alerts, history projection, and timeline render the normalized snapshot. API credit expiration reminders are not added to the earned-reset reminder schedule.

SQLite version 5 protects the new provider and grant metadata from older executables. The version rises when the API credential is staged and never decreases when another provider saves. Grant metadata is stored with existing compact-history definitions; credentials and raw responses are excluded.

- [x] Synthetic mapping covers exact dollars, empty pools, overage, unknown expiry, scheduled/held/expired grants, inconsistent data, and scope rejection
- [x] Synthetic client coverage checks journal-before-sign-in, expected identity/scope on refresh, cancellation, and owned-profile removal
- [x] Synthetic presentation and persistence cover expiration events, consumed grants, shared USD rows, history, and the monotonic version gate
- [x] Existing Chrome session reconciles the provider page with its network response
- [ ] Chrome extension and host installation succeed, and existing browser sign-in is reused
- [ ] Scrooge reads the same balance and expiration as the provider page
- [ ] Background refresh and app restart succeed without interaction
- [ ] Second-account isolation, expired session, reconnect, and remote revocation behavior are verified
- [ ] Desktop and compact native visual acceptance is complete

Follow [Provider Integration](../integration.md), [Credential Recovery](../credential-recovery.md), and [Testing](../testing.md) before broadening the support claim.

## Verification Run

The November 17 solution run passed 490 tests: 143 Core and 347 Infrastructure, including 11 API credit tests. The self-contained Windows x64 publish succeeded. These checks used the installed .NET SDK 10.0.404 from the parent directory because the repository's exact 10.0.403 SDK was unavailable; the pin was left unchanged. Verification with the pinned SDK remains open.

The native reference was the existing production dashboard, including Codex and Copilot account rows and the shared provider chooser. The rebuilt app displayed the new OpenAI API choice and opened a separate Platform sign-in window while retaining the existing account dashboard. No successful API account, compact USD-row review, or unattended refresh has yet been observed. The branch is not a published release.

## Chrome Bridge Verification

The replacement passed 498 .NET tests (143 Core and 355 Infrastructure) and nine JavaScript tests. The self-contained Windows app and native host published successfully using installed SDK 10.0.404; the unavailable 10.0.403 repository pin remains unchanged. The installed native host was registered for the current user and the rebuilt app restarted; the native dashboard retained its existing accounts. The shared production connection page and ActionButton are the UI reference. Compact connection/popup review remains open.

Browser automation refused `chrome://extensions`, so Chrome extension loading requires the user's manual action. Installation and live-provider checks remain separate from fixtures. The ordinary release package does not yet install the host or distribute the unpacked extension; see the preview setup guide.
