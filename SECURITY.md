# Security

Scrooge handles account access needed to read usage. Contributions must preserve credential isolation, verify account identity, and keep monitoring free of inference, reset redemption, purchases, and changes to other apps' accounts.

## Reporting a Vulnerability

Do not post tokens, credential files, databases, personal account details, or an exploit containing private data in a public issue.

Private vulnerability reporting was checked on November 1, 2026 and is currently disabled. If GitHub's Security tab later offers private vulnerability reporting, use that channel. If it is unavailable, open a minimal issue requesting a private reporting channel without vulnerability details. Establish the private channel with a maintainer before sending sensitive material. Enabling and documenting a permanent private reporting channel is a roadmap item; this document does not imply it is already enabled.

Provide affected versions, the security boundary involved, reproduction steps using synthetic data, impact, and a suggested mitigation if known. No response-time guarantee or supported-version policy has been established yet.

## Contributor Requirements

- Store app-managed secrets through ISecretStore and the Windows DPAPI implementation, not in SQLite or source files.
- Keep CLI profiles owned by Scrooge and isolated from existing user sessions. Validate profile identifiers and construct process arguments through ArgumentList.
- Verify provider identity and billing/workspace scope before accepting data. Reject mismatched reconnects and stale responses from retired credentials.
- Bound provider I/O, honor cancellation and retry deadlines, and return safe errors without raw response bodies or stderr.
- Remove or sign out only the selected Scrooge-owned credential/profile. Retain retryable cleanup state when retirement fails.
- Keep credentials, local databases, profiles, and private fixtures out of commits, screenshots, logs, and release packages.
- Retain third-party license notices and review new dependencies and executable download paths.

For a discovered credential leak, stop reproducing with the exposed credential and report through the private channel. The credential owner should revoke or rotate it. Do not copy it into a test fixture.

See [Architecture](docs/architecture.md#persistence-and-secrets), [Provider Integration](docs/integration.md), and [Testing](docs/testing.md).
