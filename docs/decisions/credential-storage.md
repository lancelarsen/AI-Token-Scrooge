# Credential Storage Decisions

Reviewed November 2, 2026. These decisions support the [Credential Protection Plan](../../tasks/credential-protection-plan.md); they do not certify a release.

## GitHub Sign-In

Retain the existing GitHub App device flow while the current no-client-secret distribution policy remains in place. Keep current-user DPAPI for its tokens.

GitHub's [security guide](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/best-practices-for-creating-a-github-app) prefers authorization code with PKCE outside constrained environments. Its [token-exchange documentation](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app) still requires `client_secret` for that exchange; PKCE adds `code_verifier`, rather than removing that requirement. The latter page also lists desktop applications as device-flow users. This is a documentation tension, not proof that our flow is the preferred native pattern.

The concrete migration blocker is compatibility with Scrooge's policy against distributing a client secret and its absence of a developer-hosted token-exchange backend. No PKCE prototype or registration change was performed. Do not substitute an OAuth App without rechecking billing permissions and renewal. A future proposal must resolve client handling, registered callbacks, state validation, S256, identity, cancellation, and renewal before changing production sign-in.

## Data Directory Identity

GitHub's local storage namespace uses Windows volume and directory file identity, not a hash of the supplied path spelling. Native verification found that package redirection exposed the same data through two path strings, including through the existing desktop shortcut. [Microsoft's FILE_ID_INFO documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info) describes the combined identity used to compare file handles. Copying data to a genuinely different directory still requires migration or reconnect handling.

## Codex Profile Location

Keep explicit keyring mode. Do not migrate a profile by merely moving its directory.

The reviewed [OpenAI source](https://github.com/openai/codex/blob/main/codex-rs/login/src/auth/storage.rs) derives the direct keyring lookup name from the canonical `CODEX_HOME` path. It also contains another keyring backend, so this source observation does not prove the installed binary's backend. Path changes need installed-version evidence or a reconnect transition that keeps old cleanup retryable. Scrooge must not reverse-engineer and copy live keyring payloads to work around the CLI.

## Claude Encryption

Keep the supported CLI credential-file format and restrict Windows access. Do not add a decrypt-to-file wrapper or require filesystem encryption as part of this change.

Anthropic's [authentication guide](https://code.claude.com/docs/en/authentication#credential-management) documents a Windows credential file with user-profile access controls and relocation through `CLAUDE_CONFIG_DIR`. It does not document a Windows keyring backend for this subscription flow. The API-key helper is not evidence of support for subscription usage or renewal through an external vault.

Additional encryption remains unverified, rather than impossible. Revisit it when a supported compatible interface exists. Permissions reduce access from other accounts but do not encrypt the file, protect a copied plaintext file, or exclude malware running as the same user. Crash and renewal tests remain required for the permissions implementation.
