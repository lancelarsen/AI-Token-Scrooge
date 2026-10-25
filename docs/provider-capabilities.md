# Provider evidence

The installed Codex schema, rather than the earlier conceptual mockup, drives mappings. `rateLimitsByLimitId` is preferred over legacy `rateLimits`; duration and reset timestamps can be absent. Earned-reset count is independent of optional/capped details. Optional `accountId` is checked against the saved workspace. `ordinaryUsageAllowed=false` prevents a restoration claim; an unknown permission does not cause a positive restoration event.

Two fresh app-owned keyring profiles were read through actual `codex app-server` stdio and remained unsigned-in. Neither profile inherited the current Codex login. Source architecture also namespaces keyring records by profile, but actual signed-in multi-account renewal remains unverified.

GitHub mapping uses the REST billing API with API version `2026-03-10`, current credits or legacy request units, and a current UTC month filter. Authentication is resolved through `/user` and its numeric ID is checked on refresh. Organization reports use the documented authenticated-user filter. Returned usage is not treated as a provider-reported allowance; a manually supplied allowance is labeled. Permissions, plan changes, discounts and latency still require real-account reconciliation.

Sources checked in this session: [Codex app-server](https://learn.chatgpt.com/docs/app-server), [Codex keyring implementation](https://github.com/openai/codex/blob/main/codex-rs/login/src/auth/storage.rs), [GitHub billing usage API](https://docs.github.com/en/rest/billing/usage).

General ChatGPT model quotas, OpenAI API billing and other providers remain outside this release's verified integrations.

Provider-page affordances use the official Codex usage route and separate help link, GitHub personal billing settings, or an escaped organization billing-settings route. These pages use the browser's current identity; Radar explicitly asks users to check its account/workspace before comparing. Link selection is tested, but a successful signed-in handoff is not yet verified. Reserved reset counts, partial details and expiry alerts also work when Codex supplies no quota windows; that case has mapping tests and a native demo observation.
