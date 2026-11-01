# Contribution Roadmap

Contributions are welcome. These are open tasks, not promises of provider availability or delivery dates. Check the current implementation and related issues before starting. Older implementation cards remain in [Implementation Tasks](todo.md) and [History Tasks](history-todo.md).

## Additional Providers

Each candidate starts with the evidence and acceptance process in [Provider Integration](../docs/integration.md). Research should establish the product, sign-in method, read-only usage source, units, allowance, timestamps, account scope, and permission requirements. No inference request may be used as a usage probe.

- [ ] Grok / xAI: investigate consumer subscription usage separately from API billing; document whether a supported browser or CLI connection can expose usable quota data
- [ ] Google Gemini: investigate the specific consumer or developer product and its available usage source before proposing a connector
- [ ] Cursor: investigate account usage, allowances, reporting delay, and safe sign-in support
- [ ] Windsurf: investigate account usage, plan semantics, refresh times, and safe sign-in support
- [ ] Other community-requested services: open a proposal with read-only data-source evidence and account-isolation expectations
- [ ] Define a separate proposal for API billing/credit balances if they do not fit subscription quota semantics

A provider is ready only after mapping and failure tests, identity/scope isolation, reconnect/removal, native UI reuse, and read-only live reconciliation. Unsupported fields must stay unknown. A model's existence or an inference SDK does not establish usage-monitoring support.

## Contributor Foundations

- [ ] Choose and add a repository-wide LICENSE, then document contribution terms consistently
- [ ] Enable and verify private vulnerability reporting; document the reporting channel and supported-version policy
- [ ] Add pull-request CI with read-only permissions and no release publishing or repository secrets for untrusted forks
- [ ] Add focused bug-report and provider-proposal issue forms
- [ ] Add automated Markdown/link checks and a maintainable shared design-token source
- [ ] Reconcile older design and verification notes with current behavior without erasing historical evidence

## Provider Reliability

- [ ] Expand multi-account renewal, rejected reconnect, and removal-isolation checks across all integrated providers
- [ ] Reconcile GitHub personal and organization billing, credit/request units, allowance changes, and reporting latency
- [ ] Add compatibility evidence for internal GitHub entitlement changes and safe unknown-allowance behavior
- [ ] Expand Claude output-format, locale, CLI-version, missing-field, and process-cancellation coverage
- [ ] Audit CLI executable discovery across supported installation methods
- [ ] Consider an explicit provider-dispatch abstraction once a new connector demonstrates the need; retain current account/lifecycle contracts

## Design and Accessibility

- [ ] Complete keyboard and screen-reader review of custom timeline, threshold, quiet-hours, and polling controls
- [ ] Expand desktop/compact and DPI checks with long names, unknown plans, many accounts, and wrapped color palettes
- [ ] Cover day-anchor geometry and timestamp positioning through rendered history/filter/resize/DST regressions
- [ ] Confirm the new retention setting visually in the rebuilt Windows app at desktop and compact sizes
- [ ] Audit shared UI copy, Title Case, help placement, and empty-state provider lists

## Windows Release and Performance

- [ ] Produce a signed Windows installer and document a supported upgrade/uninstall path
- [ ] Verify startup, notification activation, tray restoration, and single-instance recovery on a clean Windows user
- [ ] Complete extended memory/CPU and long-history tests; investigate the regression in [Performance Notes](../docs/native-performance.md)
- [ ] Add artifact verification on a clean machine and document the supported Windows matrix
- [ ] Evaluate safe update notifications and release retention without introducing unsolicited downloads or installation

## Data and Recovery

- [ ] Add broader migration, interrupted-write, disk-full, and cleanup-retry coverage
- [ ] Verify retention changes consistently affect alerts and history across restart and boundary timestamps
- [ ] Design a redacted diagnostic export that excludes credentials and account identifiers by default
- [ ] Define backup/export behavior before adding a user-facing history export feature

Use [Contributing](../CONTRIBUTING.md) to propose scope and report verification. Mark an item complete only with evidence appropriate to the behavior, not solely because a scaffold or fixture exists.
