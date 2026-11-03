# Contribution Roadmap

Contributions are welcome. These are open tasks, not promises of provider availability or delivery dates. Check the current implementation and related issues before starting. Older implementation cards remain in [Implementation Tasks](todo.md) and [History Tasks](history-todo.md).

## Additional Providers

Each candidate starts with the evidence and acceptance process in [Provider Integration](../docs/integration.md). Research should establish the product, sign-in method, read-only usage source, units, allowance, timestamps, account scope, and permission requirements. No inference request may be used as a usage probe.

- [ ] Grok / xAI: investigate consumer subscription usage separately from API billing; document whether a supported browser or CLI connection can expose usable quota data
- [ ] Google Gemini: investigate the specific consumer or developer product and its available usage source before proposing a connector
- [ ] Cursor: investigate account usage, allowances, reporting delay, and safe sign-in support
- [ ] Windsurf: investigate account usage, plan semantics, refresh times, and safe sign-in support
- [ ] Other community-requested services: open a proposal with read-only data-source evidence and account-isolation expectations

A provider is ready only after mapping and failure tests, identity/scope isolation, reconnect/removal, native UI reuse, and read-only live reconciliation. Unsupported fields must stay unknown. A model's existence or an inference SDK does not establish usage-monitoring support.

## Live Provider Testing

- [ ] Test reconnecting and removing accounts across the integrated providers
- [ ] Verify that multiple connected accounts remain isolated during usage updates and credential renewal
- [ ] Compare reported usage with the provider's own usage screen for supported account types

## Credential Protection

- [ ] Execute the [Credential Protection Plan](credential-protection-plan.md): retain Codex keyring and GitHub DPAPI, verify/harden Claude file permissions, and review optional encryption and GitHub PKCE compatibility. Keep documented behavior separate from verified release evidence

## Windows Installer

Date-based versions and portable updates are being implemented. The [Update Guide](../docs/app-updates.md) defines the release and native acceptance requirements; this does not complete the signed-installer task below.

- [ ] Provide a signed Windows installer

Use [Contributing](../CONTRIBUTING.md) to propose scope and report verification. Mark an item complete only with evidence appropriate to the behavior, not solely because a scaffold or fixture exists.
