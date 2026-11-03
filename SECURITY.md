# Security

Scrooge handles account access needed to read usage. Contributions must preserve credential isolation, verify account identity, and keep monitoring free of inference, reset redemption, purchases, and changes to other apps' accounts.

## Credential Storage Policy

Use provider-supported credential storage, with the strongest compatible Windows protection. Do not force CLI-managed credentials into a store the CLI cannot read or refresh. Reviewed against official documentation on November 2, 2026:

| Provider | Current Implementation | Required Treatment |
| --- | --- | --- |
| OpenAI Codex | Explicit `cli_auth_credentials_store="keyring"` in the isolated Codex process | Keep keyring mode; do not substitute `auto`, which can fall back to plaintext. Verify behavior with supported installed versions |
| GitHub | Current-user DPAPI through WindowsSecretStore for access/refresh tokens and optional advanced tokens | Preserve encryption, restrict file access, and verify renewal and cleanup. DPAPI is Scrooge's Windows choice, not a GitHub-specific requirement |
| Claude | CLI-managed `.credentials.json` in the isolated CLAUDE_CONFIG_DIR; Scrooge adds no encryption | Verify/harden Windows file permissions and preserve CLI compatibility. The reviewed Anthropic guide does not document Windows keyring support; do not claim this file is encrypted |

SQLite settings and usage history remain unencrypted by design. Credentials must stay separate. The local implementation puts GitHub credentials under `%LOCALAPPDATA%\AIResetRadar\Security\<data-directory-id>\Credentials`, independently of `--data-dir`. It verifies an encrypted destination copy before retiring a legacy record. New CLI sign-ins use Profiles or ClaudeProfiles under that Security namespace. Existing CLI profiles remain at their saved legacy location until a verified migration or reconnect; their migration remains open. Account and cleanup state retain the explicit location, and a database version gate prevents older builds from discarding that reference.

Local hardening changes add explicit Windows permissions for credential/profile directories, bounded DPAPI reads, per-path gates and OS file leases for secret-store operations, and encrypted temporary-file cleanup. Claude's credential-file permissions are also reapplied around CLI execution. These are working-tree changes, not proof of protection in a published build. The [Verification Checkpoint](docs/credential-protection-verification.md) records tests and a native monitoring check. Cross-user access, CLI renewal/crash behavior, and full release verification remain acceptance requirements.

Scrooge's monitoring is read-only, but stolen credentials may grant broader provider access. DPAPI and OS keyrings do not establish protection against malware running as the same user or a compromised administrator/OS. Directory isolation and Windows permissions are not equivalent to encryption. Removing a local credential also does not necessarily revoke it at the provider.

## Application Update Downloads

Update checks use the public GitHub release API without provider credentials. Downloads are limited to the project's versioned Windows package and GitHub release-asset hosts over HTTPS. The updater checks GitHub's SHA-256 digest, byte count, embedded version/protocol, required files, and ZIP paths before handoff. No update step runs elevated. Releases remain unsigned; checksums are not publisher signatures. See [App Versions and Updates](docs/app-updates.md#network-and-trust) for the exact boundary and local staging behavior.

## Sign-In Review

GitHub's current guidance prefers authorization code with PKCE over device flow outside constrained environments. Scrooge currently uses GitHub App device flow. Review PKCE compatibility, registration and callback requirements, token renewal, and public-client handling before changing it. Do not assume a client secret shipped in a desktop binary can remain confidential. This review does not change the DPAPI storage decision.

The same GitHub guide says authorization-code token exchange requires a client secret, even for a public client. Scrooge currently does not distribute one. Resolve that constraint in the sign-in review before changing the flow; never ship a GitHub App private key or describe an embedded public-client secret as confidential.

The [Credential Protection Plan](tasks/credential-protection-plan.md) tracks migration, lifecycle, and release checks. The [Storage Decisions](docs/decisions/credential-storage.md) retain device sign-in under the current distribution policy and Claude's supported file format. Additional Claude encryption requires compatibility evidence; a temporary decrypt-to-file wrapper is not the default solution.

## Official References

- [OpenAI Credential Storage](https://learn.chatgpt.com/docs/auth?surface=cli#credential-storage)
- [GitHub App Security and Sign-In Guidance](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/best-practices-for-creating-a-github-app)
- [Claude Credential Management](https://code.claude.com/docs/en/authentication#credential-management)
- [Microsoft DPAPI](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)

These sources establish documented behavior and guidance, not verification of an installed CLI or downloadable release.

## Reporting a Vulnerability

For connection failures or suspected credential exposure, see [Credential Recovery](docs/credential-recovery.md). It separates local removal from provider revocation and identifies the unverified Codex CLI revocation path.

Do not post tokens, credential files, databases, personal account details, or an exploit containing private data in a public issue.

Private vulnerability reporting was checked on November 1, 2026 and is currently disabled. If GitHub's Security tab later offers private vulnerability reporting, use that channel. If it is unavailable, open a minimal issue requesting a private reporting channel without vulnerability details. Establish the private channel with a maintainer before sending sensitive material. Enabling a permanent private reporting channel remains a maintainer decision.

Provide affected versions, the security boundary involved, reproduction steps using synthetic data, impact, and a suggested mitigation if known. No response-time guarantee or supported-version policy has been established yet.

Once a private channel is available, use this report outline: affected app/build and CLI versions; Windows version; expected and observed behavior; reproduction using synthetic data; affected credential boundary; potential impact; and a proposed fix, if available. Omit live tokens, account identifiers, credential files, databases, and unredacted screenshots. A maintainer should acknowledge the report privately, reproduce with synthetic data, agree on disclosure timing, and document the verified fix and any remaining limits. This outline is prepared guidance; it does not enable a reporting channel or promise a response deadline.

## Contributor Requirements

- Store app-managed secrets through ISecretStore and the Windows DPAPI implementation, not in SQLite or source files.
- Hold the shared credential operation lease across GitHub renewal and removal. A retry from memory must verify that its expected saved record still exists before replacing it; it must never restore a removed or newer credential.
- Keep CLI profiles owned by Scrooge and isolated from existing user sessions. Validate profile identifiers and construct process arguments through ArgumentList.
- Verify provider identity and billing/workspace scope before accepting data. Reject mismatched reconnects and stale responses from retired credentials.
- Bound provider I/O, honor cancellation and retry deadlines, and return safe errors without raw response bodies or stderr.
- Launch only absolute installed CLI executable paths without shell/PATH fallback, reject redirected executables, and apply CliEnvironment before setting the isolated profile. Do not pass inherited API keys or alternate provider credentials into monitoring processes. The explicit environment filter is not an exhaustive secret detector.
- Remove or sign out only the selected Scrooge-owned credential/profile. Retain retryable cleanup state when retirement fails.
- Journal a fresh credential ID before sign-in can persist credentials. Clear that intent atomically with account acceptance, or only after cleanup succeeds. Keep tokens and account identities out of the journal. Remove CLI profile files only after successful logout, with ownership, containment, and redirected-path checks.
- Keep credentials, local databases, profiles, and private fixtures out of commits, screenshots, logs, and release packages.
- Retain third-party license notices and review new dependencies and executable download paths.

For a discovered credential leak, stop reproducing with the exposed credential and report through the private channel. The credential owner should revoke or rotate it. Do not copy it into a test fixture.

See [Architecture](docs/architecture.md#persistence-and-secrets), [Provider Integration](docs/integration.md), and [Testing](docs/testing.md).
