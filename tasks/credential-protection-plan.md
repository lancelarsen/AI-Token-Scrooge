# Credential Protection Plan

Status: Revised after official documentation and working-tree review, November 2, 2026. Some hardening exists in local code; the remaining tasks and release verification are incomplete. This plan does not establish that those changes have shipped. This documentation review did not open live credential files, authenticate accounts, or capture network traffic.

## Scope and Security Goal

Protect authentication credentials while retaining browser sign-in, account isolation, token renewal, and unattended monitoring. Leave SQLite history and settings unencrypted as requested. Keep all secrets out of that database, logs, screenshots, source control, and release artifacts.

The goal is to reduce credential theft through copied files, other standard Windows accounts, unsafe directories, accidental disclosure, and failed cleanup. It is not possible to promise zero risk or protection against an administrator, a compromised OS, or malware already running as the signed-in user. Provider tokens may permit more than Scrooge's read-only operations; the app's command restrictions do not reduce the permissions of a stolen token.

## Current Evidence

| Area | Source-Verified State | Remaining Gap |
| --- | --- | --- |
| GitHub | Current-user DPAPI, explicit permissions, bounded reads, encrypted temporary-file cleanup, and an OS lease across renewal/removal. Pending replacements cannot restore removed or newer credentials; synthetic concurrency tests pass | Verify live renewal, access from a second standard Windows account, and the release artifact |
| Codex | CodexConnection.cs requests keyring storage, isolates CODEX_HOME, disables analytics, and restricts RPC methods | Verify keyring behavior and absence of plaintext fallback with supported installed versions and Windows accounts |
| Claude | ClaudeClient.cs isolates CLAUDE_CONFIG_DIR. Local changes restrict the profile directory and existing credential file before CLI execution and reapply file permissions afterward | Verify permissions through login, renewal, cancellation, and crashes; no additional encryption is implemented |
| Storage Location | GitHub records migrate to a local Security namespace independently of --data-dir. New CLI sign-ins use that root with explicit persisted location references and a database version gate | One user-completed Codex reconnect polls successfully at the new root and its old profile was retired; remaining Codex/Claude transitions are open |
| Retirement | A durable journal covers unfinished sign-ins; account acceptance clears it transactionally. Startup retries abandoned credentials, and CLI logout is followed by validated profile deletion. Synthetic failure tests pass | After the user approved retrying with the 90-second deadline, one retired Codex profile was removed and its queue entry cleared. Broader lifecycle behavior remains unverified; local deletion is not necessarily remote revocation |
| Packaging | Local changes add Test-PublishContent.ps1 to reject redirected paths, credential filenames/backups, and recognizable secret content in selected text formats | Validate the final ZIP; pattern matching is not an exhaustive secret scan |
| CLI Boundary | Absolute installed executable paths, redirected-path rejection, explicit environment filtering, bounded Claude output/deadlines, and cancellation tests are implemented. Both installed CLI signatures were valid during inspection | Filters do not detect arbitrary environment secrets; path checks do not enforce publisher signatures on every launch |

Source references: [Secret Store](../src/AIResetRadar.Infrastructure/SecretStore.cs), [GitHub Renewal](../src/AIResetRadar.Infrastructure/GitHubCredentialManager.cs), [Codex](../src/AIResetRadar.Infrastructure/CodexConnection.cs), [Claude](../src/AIResetRadar.Infrastructure/ClaudeClient.cs), [Account Lifecycle](../src/AIResetRadar.App/MainPage.Accounts.cs), and [Packaging](../scripts/Package-Windows.ps1).

Source inspection establishes what the working tree attempts to enforce. It does not establish live CLI compatibility, cross-user protection, or the behavior of an already published build. Keep the acceptance tasks open until their evidence is recorded.

The [Verification Checkpoint](../docs/credential-protection-verification.md) records passing tests, the native monitoring check, and observed GitHub migration. [Storage Decisions](../docs/decisions/credential-storage.md) records the PKCE compatibility blocker and the decision to retain Claude's supported file format. These decisions close the research questions for this iteration; live lifecycle and release acceptance remain open.

## Decisions

Use provider-supported credential storage, with the strongest compatible Windows protection. Different providers can use different mechanisms; file permissions are not encryption.

1. Retain current-user DPAPI for app-managed secrets. Do not use machine-wide DPAPI or invent an encryption algorithm/master key. The hard-coded optional entropy is not a secret key and should not be advertised as one. Preserve compatibility with existing encrypted records.
2. Keep Codex's explicit `cli_auth_credentials_store="keyring"`. OpenAI documents that this mode fails when the store is unavailable; `auto` can fall back to plaintext. Verify installed-version behavior without duplicating credentials into a second vault.
3. Put credentials and CLI profiles in a dedicated local, per-user security root independent of a custom database directory. Apply explicit Windows permissions before creating sensitive files. Test ordinary users separately from administrators; do not imply administrators are excluded.
4. Retain Claude's documented CLI-managed credential file as the compatibility baseline and verify/harden its Windows permissions. Anthropic documents no Windows keyring option in the reviewed authentication guide. Additional encryption is research, not a required replacement or an established provider recommendation. Do not assume API-key helpers or setup-token authentication support subscription /usage or refresh-token renewal.
5. Do not claim all credentials are encrypted. Keep the Claude README caveat unless a compatible encryption design is implemented and its entire credential lifecycle passes runtime and crash tests.
6. Review GitHub authorization-code sign-in with PKCE separately from storage. GitHub recommends platform-appropriate token storage, not DPAPI specifically; current-user DPAPI is our Windows implementation choice. Device flow remains the current implementation until a replacement is proved.

## Ordered Implementation Tasks

### 1. Establish the Windows Credential Baseline

Dependencies: None. Likely files: a focused Windows credential test harness and sanitized evidence under docs/verification.

- Inventory filenames, permissions, CLI versions, and credential-store behavior using isolated test accounts when available. The user has only everyday accounts: preserve those connections, use synthetic credentials for destructive tests, and leave live destructive acceptance unverified. Do not print or persist token values.
- Verify Codex keyring use, Claude's credential-file location, and any copies/backups after sign-in, polling, refresh, logout, and forced termination.
- Record supported Windows editions/filesystems and a second standard-user access result. Capture only sanitized metadata; live tokens must not enter fixtures.

The user has no second Windows login available. Cross-user access/decryption and fresh-user package checks remain unverified. A synthetic isolation probe is prepared; its owner-account check passed and its different-user mode correctly refused a same-user run. Do not create another Windows account or treat these results as cross-user acceptance. Resume that check when a separate authorized standard-user environment is available.

Acceptance: A documented compatibility matrix distinguishes verified results from unknowns, including actual token capabilities where provider documentation permits establishing them.

### 2. Harden the Existing Secret Store and Location

Dependencies: Task 1. Likely files: SecretStore.cs, AppServices.cs, a Windows storage-policy helper, and focused tests.

- Create a per-user local security root with explicit permissions before writing secrets. Validate canonical paths and reject junction/symlink escapes and unsafe roots; do not rewrite permissions recursively on arbitrary user-selected folders.
- Keep DPAPI CurrentUser, validated IDs, encrypted temporary writes, and short-lived plaintext buffers. Add bounded reads and serialized updates/cleanup. Treat corrupt or undecryptable data as reconnect-required with a safe message; never fall back to plaintext.
- Design crash-safe migration from existing default/custom locations: stop competing operations, protect and verify the destination, commit the new reference, then retire the old owned copy. Preserve retry state without storing secrets in it. Avoid plaintext migration backups and silent data loss.

Acceptance: Same-user round trip succeeds, a second standard user cannot read/decrypt, corrupted data fails safely, unsafe paths are rejected, and interrupted writes/migrations recover without exposing a token or losing a known-good credential. Managed strings and process memory cannot be guaranteed erased.

### 3. Verify Claude's Supported Storage and Assess Optional Encryption

Dependencies: Task 1; permission changes reuse Task 2's storage policy. Likely files: ClaudeClient.cs, Windows storage-policy helper, focused tests, and a decision note.

- Keep the subscription browser-login flow and isolated CLAUDE_CONFIG_DIR. Anthropic documents `.credentials.json` inside that directory on Windows, protected by inherited user-profile permissions. Verify the actual file and effective permissions for supported CLI versions; isolation alone does not establish access protection.
- Verify/harden permissions before credential creation and after CLI replacement or refresh writes. Exercise login, identity, /usage, renewal, two-account isolation, logout, cancellation, concurrent operations, forced termination, and restart. Preserve the CLI's expected file format.
- Investigate an OS vault only if a supported interface demonstrably works with subscription usage and renewal. Do not infer compatibility from API-key helpers, macOS Keychain support, or Codex's settings.
- Treat transparent filesystem encryption as optional research with Windows edition, filesystem, and recovery-key constraints. Do not make an unverified encryption feature a new prerequisite for Claude support.
- Do not implement a decrypt-to-file DPAPI wrapper by default. Such a wrapper creates plaintext exposure during use and after some crashes; cleanup does not guarantee removal from SSDs, journals, or backups. Any proposal needs a separate compatibility and residual-risk review.

Acceptance: Document and test the supported permission-protected baseline. Record whether additional encryption is supported, unsupported, or unverified. A completed permissions task must not be presented as encrypted-at-rest storage. Optional encryption research may conclude with a documented decision to keep the supported baseline; it must not silently change sign-in or disable Claude.

### 3A. Review GitHub Sign-In Against Current Guidance

Dependencies: Task 1. This is an authentication decision, separate from DPAPI storage hardening. Likely files: a decision note and an isolated authorization-flow harness before any production transport changes.

- Compare the existing GitHub App device flow with authorization code plus PKCE. GitHub's guidance prefers PKCE over device flow outside constrained environments; do not label our existing flow the preferred desktop pattern.
- Verify GitHub App versus OAuth App requirements, app registration, public-client/client-secret handling, exact callback support, state validation, PKCE S256, cancellation, token expiry/renewal, and account identity. Do not import assumptions from generic OAuth examples or treat a bundled public-client secret as confidential.
- GitHub's reviewed guide says its authorization-code flow requires a client secret, including for public clients where it cannot remain confidential. Record that constraint explicitly. Our current policy is not to distribute a client secret; resolve that policy and compatibility question before proposing a production PKCE migration. Never bundle a GitHub App private key.
- Record whether migration works with the current app and permissions, what registration changes it requires, and a recommendation. Preserve working device sign-in until the replacement is demonstrated. A production flow or registration change needs its own implementation scope and tests.

Acceptance: A source-backed decision and prototype evidence, or a specific documented compatibility blocker. Token storage remains current-user DPAPI; do not replace it merely to change the sign-in flow.

### 4. Complete Credential Retirement and Leakage Checks

Dependencies: Tasks 2 and the Task 3 supported storage baseline. Likely files: AccountMonitor.cs, account lifecycle orchestration, provider credential helpers, packaging script, and focused tests. Split retirement and packaging into separate patches.

- Cover canceled/rejected sign-ins, reconnect, account removal, upgrade migration, and pending cleanup. Preserve existing persist-before-retire ordering and prevent stale reads from restoring removed credentials.
- Clean only verified Scrooge-owned paths and keyring entries, including safe temporary-file cleanup. Distinguish local removal from provider-side revocation. Document provider revocation steps where remote revocation is unavailable; never revoke unrelated user sessions or a shared PAT automatically.
- Never put secrets in command arguments, URLs, exception messages, exports, or logs. Audit inherited environment and executable resolution; use the expected official CLI and avoid arbitrary PATH/shim substitution. Verify redirects and identity checks remain enforced.
- Expand release exclusions and use synthetic sentinel tokens to check SQLite, logs, temporary files, and ZIPs. GitHub's public client ID is not a secret. Do not add a client secret to the desktop app.

Acceptance: Fault-injection tests show that retirement remains retryable and isolated; synthetic credentials never appear in non-secret output or packages. Remote revocation status is not falsely reported as complete.

### 5. Verify and Communicate the Shipped Result

Dependencies: Tasks 1-4, including the Task 3A sign-in decision.

Documentation prepared: [Credential Recovery](../docs/credential-recovery.md), the [Provider Credential Checklist](../docs/integration.md#credential-checklist), and the private-report outline in [Security](../SECURITY.md#reporting-a-vulnerability). Provider recovery links were reviewed on November 2, 2026. No live revocation was attempted. OpenAI's session-management guide excludes Codex CLI sessions, so remote CLI revocation remains an explicit verification gap. The private reporting channel has not been enabled.

- Run focused regression tests and the full relevant suite. Build and restart the Windows app; verify native connect, polling, renewal, reconnect, removal, and recovery with isolated accounts.
- Verify the exact release artifact under a fresh standard Windows account. Record artifact hash, app/CLI versions, and evidence date. Check observed network destinations for app and child processes; endpoint metadata alone does not prove request-body contents. Avoid storing live authorization headers in captures.
- Update README, SECURITY.md, Architecture, Provider Integration, and release evidence to describe verified protections and limitations. Add a credential checklist to provider contributions.
- Prepare a private vulnerability-reporting channel and recovery instructions. Enabling the channel is a separate repository setting change, not performed by this plan. Keep signed Windows distribution as an existing related roadmap item; encryption does not prove an executable is trustworthy.

Acceptance: No known unresolved credential exposure within the stated threat model is described as fixed. Every public protection claim maps to source and runtime evidence. Unverified external CLI behavior remains identified.

## Sources and Limits

- [OpenAI Credential Storage](https://learn.chatgpt.com/docs/auth?surface=cli#credential-storage): explicit keyring mode fails if unavailable; auto can fall back to a file. This is a supported option, not a claim that OpenAI mandates it.
- [GitHub App Security](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/best-practices-for-creating-a-github-app): native-client platform storage guidance and preference for PKCE over device flow outside constrained environments.
- [Microsoft DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata): user-bound encryption and integrity protection; same-user processes remain within the security boundary. Roaming/recovery circumstances limit blanket claims about machine binding.
- [Claude Credential Management](https://code.claude.com/docs/en/authentication#credential-management): Windows file storage and CLAUDE_CONFIG_DIR behavior. This does not establish a supported encrypted Windows credential backend.

Reviewed November 2, 2026. Local implementation and verification are recorded in the [Verification Checkpoint](../docs/credential-protection-verification.md). GitHub credential migration and normal monitoring were observed. Remaining lifecycle, cross-user, and release checks are incomplete; no security certification or repository setting change is implied.
