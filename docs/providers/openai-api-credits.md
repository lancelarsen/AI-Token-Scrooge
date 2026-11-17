# OpenAI API Credit Pool

Scrooge's API credit work tracks the full API organization's dollar balance, including purchased and promotional credits. It is separate from Codex subscription windows and earned resets. The monthly subscription benefit amount is not the current credit balance.

## Status and Evidence

Status: Implemented With Fixtures; isolated sign-in and background refresh acceptance remain open.

Research date: November 17, 2026. Repository baseline: `c799788`. The API dashboard has no published version identifier for the internal endpoints observed below.

A read-only check in an existing authorized Chrome session reconciled the billing overview, credit-grant table, and the dashboard's credit-grant response. Reloading the grant page returned the same balance. Only field names and sanitized findings belong in this document; live account identifiers, grant identifiers, amounts, and raw responses are omitted.

The Windows connection was built and opened. Its separate browser profile reached provider sign-in, but the user reported an error during Google sign-in. This does not establish a usable recurring connection. Keep the implementation's live acceptance open until sign-in, refresh, restart, and cleanup have been exercised.

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

The current adapter uses a dedicated WebView2 profile. It observes the provider page's own identity and billing responses, retaining only the stable user ID, organization membership, and normalized credit fields. It does not copy browser cookies or export session tokens. The user selects the organization and confirms it; a reload verifies identity and scope before acceptance. Background reads reject a different user or organization and do not open interactive sign-in.

The profile is journaled before sign-in and stored under the app's local Security namespace in `OpenAiBillingProfiles/<credential-id>`. Existing operation leases serialize reads and retirement. Windows directory permissions and WebView2-managed storage apply; Scrooge does not claim all browser-profile contents are encrypted. Removal deletes only the owned profile through retryable cleanup. Local removal does not establish remote session revocation.

Google's [embedded-browser policy](https://developers.googleblog.com/en/upcoming-security-changes-to-googles-oauth-20-authorization-endpoint-in-embedded-webviews/) is a compatibility constraint for this approach. Do not work around it by spoofing a browser identity or copying another browser's session. A provider-supported external-browser flow or another compatible authentication route must be verified before claiming Google sign-in support.

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

`OpenAiBilling` parses identity and grants. `OpenAiBillingClient` owns connection, leases, identity checks, and retirement. The Windows browser adapter owns the browser lifecycle. The shared account cards, capacity alerts, history projection, and timeline render the normalized snapshot. API credit expiration reminders are not added to the earned-reset reminder schedule.

SQLite version 5 protects the new provider and grant metadata from older executables. The version rises when the API credential is staged and never decreases when another provider saves. Grant metadata is stored with existing compact-history definitions; credentials and raw responses are excluded.

- [x] Synthetic mapping covers exact dollars, empty pools, overage, unknown expiry, scheduled/held/expired grants, inconsistent data, and scope rejection
- [x] Synthetic client coverage checks journal-before-sign-in, expected identity/scope on refresh, cancellation, and owned-profile removal
- [x] Synthetic presentation and persistence cover expiration events, consumed grants, shared USD rows, history, and the monotonic version gate
- [x] Existing Chrome session reconciles the provider page with its network response
- [ ] Isolated provider sign-in succeeds
- [ ] Scrooge reads the same balance and expiration as the provider page
- [ ] Background refresh and app restart succeed without interaction
- [ ] Second-account isolation, expired session, reconnect, and remote revocation behavior are verified
- [ ] Desktop and compact native visual acceptance is complete

Follow [Provider Integration](../integration.md), [Credential Recovery](../credential-recovery.md), and [Testing](../testing.md) before broadening the support claim.

## Verification Run

The November 17 solution run passed 490 tests: 143 Core and 347 Infrastructure, including 11 API credit tests. The self-contained Windows x64 publish succeeded. These checks used the installed .NET SDK 10.0.404 from the parent directory because the repository's exact 10.0.403 SDK was unavailable; the pin was left unchanged. Verification with the pinned SDK remains open.

The native reference was the existing production dashboard, including Codex and Copilot account rows and the shared provider chooser. The rebuilt app displayed the new OpenAI API choice and opened a separate Platform sign-in window while retaining the existing account dashboard. No successful API account, compact USD-row review, or unattended refresh has yet been observed. The branch is not a published release.
