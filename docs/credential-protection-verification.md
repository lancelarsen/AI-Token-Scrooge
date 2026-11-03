# Credential Protection Verification

## November 2, 2026 Checkpoint

This is evidence for the local working tree, not a published release. No live credential contents were printed or saved as test fixtures. Initial checks retained the user's everyday connections. The user later completed one Codex reconnect, described below; no manual provider-side revocation was performed.

## Environment and CLI Compatibility

The local follow-up checked Windows 11 Home, build 26300.9818 (display version 26H2), with NTFS on both the credential volume (C:) and workspace volume (D:). This is one development machine, not a supported-platform certification. Version-only CLI commands returned the following; no sign-in, logout, or credential reads were requested by those commands.

| Integration | Installed CLI | Live Evidence in This Checkpoint | Still Unverified |
| --- | --- | --- | --- |
| Codex | 0.166.0-alpha.5.1 | Existing-account polling; one browser reconnect and healthy polling from the new Security root; one old-profile retirement | Renewal, crash recovery, keyring backend/failure behavior, other Windows users and CLI versions |
| Claude | 2.1.310 | Existing-account polling and profile/credential-file permission metadata | New-root sign-in, renewal/replacement permissions, two-account isolation, logout and crash recovery |
| GitHub | No CLI used | Normal billing polling and migration to the protected local store | Deliberate live token rotation/failure recovery, different-user access/decryption, remote revocation |

## Automated Evidence

- Infrastructure tests: 269 passed, zero failed or skipped.
- Core tests: 118 passed, zero failed or skipped.
- Included Windows credential storage tests: 16 passed. Cases cover real current-user DPAPI round trips, legacy format compatibility, corrupt/oversized records, failed replacement, encrypted temporary-file cleanup, concurrent writes, a separate process holding storage and whole-operation leases, cancellation, migration verification, interrupted cleanup, removal ordering, and directory-alias identity.
- GitHub credential-manager tests: nine passed. Separate manager/store instances coordinate renewal and removal, unrelated credentials remain usable, and failed-save retries cannot restore a removed or newer credential. HTTP responses are synthetic; no live refresh token was deliberately rotated for these tests.
- Added 18 lifecycle regression cases: seven journal tests, five monitor cases, and six profile cleanup cases. They cover restart recovery, atomic acceptance/rollback, stage-before-save, failed writes and deletion, unknown providers, locked files, and redirected profiles/descendants. All use synthetic credentials and temporary directories.
- CLI boundary tests: 12 passed. Synthetic subprocesses exercise stdout/stderr limits, private-text suppression, deadline/caller cancellation, no launch after cancellation, environment filtering, absolute profile arguments, and redirected executable discovery. Cancellation tests verify termination of the directly launched process and an owned descendant while its parent remains running. The descendant test holds the exact child process handle before cancellation and verifies its exit. Independently detached descendants and descendants whose parent has already exited were not tested.
- Profile-location coverage adds seven cases: legacy JSON compatibility, explicit root selection without fallback, cleanup isolation, reconnect reference preservation, journal restart/acceptance and version gating, rejected location changes, and rejection of mismatched acceptance. One Codex browser reconnect at the new location was subsequently observed; broader lifecycle coverage remains open.
- Three additional cleanup cases cover cancellation before filesystem work starts, retry after a background deletion failure, and durable cleanup completion when the caller cancels after successful secret removal.
- A read-only cache fixture reproduces the deletion failure and verifies that cleanup clears the attribute only inside the selected profile. Two virtual-time orchestration cases verify that cleanup can exceed eight seconds, that the 90-second deadline retains pending retirement, and that a later retry preserves the active account.
- Packaging fixtures: public client ID accepted; six credential/state fixtures rejected without printing the synthetic secret.
- Credential leakage tests: three passed using real Windows DPAPI, temporary SQLite storage, and synthetic HTTP responses. Accepted sign-in/removal, rejected sign-in, and failed acceptance transactions keep access/refresh sentinels out of account/snapshot output, request URLs, and error text. File scans at lifecycle checkpoints cover the fixture's database, any present SQLite sidecars, encrypted credential files, and remaining temporary/lease files for UTF-8 and UTF-16 sentinel bytes. Successful protected reads confirm that both tokens were actually saved. These tests do not exercise native crash logging or external CLI diagnostics.
- Release publish completed for Windows x64 with self-contained .NET and Windows App SDK.

Commands used:

```powershell
dotnet test tests/AIResetRadar.Infrastructure.Tests/AIResetRadar.Infrastructure.Tests.csproj --nologo --verbosity minimal
dotnet test tests/AIResetRadar.Core.Tests/AIResetRadar.Core.Tests.csproj --nologo --verbosity minimal
dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o artifacts/publish/credential-profile-root-win-x64 --nologo
./scripts/Test-PublishContent.Tests.ps1
./scripts/Package-Windows.ps1 -PublishDirectory artifacts/publish/credential-profile-root-win-x64
```

## Native and Local Evidence

Source review of the Windows unhandled-exception handler confirms that it writes the timestamp, exception type, HRESULT, and stack trace without the exception message. A native crash with a synthetic credential was not induced, and CLI-managed diagnostic files were not scanned for live tokens.

The rebuilt executable was launched with an isolated demo data directory and visually checked through Windows Computer Use. It was then restarted with normal app data and finally with the exact data-directory arguments from the existing desktop shortcut. All four existing account cards rendered, and their saved-value/refreshing indicators cleared after polling. This establishes a normal monitoring smoke check, not comprehensive provider lifecycle acceptance.

Filename and ownership inspection before restart found two Codex profile directories with no `auth.json` and two Claude profiles containing `.credentials.json`. Absence of a file does not independently prove which keyring backend the installed CLI uses. No credential file contents were inspected for this inventory.

The CLI launch review found absolute installation paths and no shell or PATH fallback. Before the CLI-boundary build, Get-AuthenticodeSignature reported Valid for the installed Claude executable, signed by Anthropic, PBC, and the installed Codex executable, signed by OpenAI OpCo, LLC. This was a point-in-time inspection; Scrooge does not enforce publisher signatures on every launch. Anthropic documents its Windows signing identity in [Advanced Setup](https://code.claude.com/docs/en/setup#platform-code-signatures). The environment filter retains Windows/proxy settings and removes its explicit provider credential and runtime-override list; it is not an exhaustive secret detector.

GitHub migration was observed after normal startup: zero `.bin` records remained in the legacy Credentials folder; one existed under the new local Security root with a protected DACL and current-user ownership. The GitHub usage row refreshed. A subsequent shortcut check exposed two Windows path aliases selecting different namespaces. File identity confirmed that they reference the same data. Namespace selection was changed to volume plus directory file ID, the encrypted record was migrated to that namespace, and a regression test was added. No user reconnect was required.

The initial storage build was launched using the desktop shortcut's original arguments. At 10:17 PM Central, all four account rows rendered without saved-value, offline, or reconnect errors. Metadata inspection confirmed one protected encrypted record in the Security root and zero legacy `.bin` records. The shortcut target was updated to the checked build while retaining its arguments; the app's existing startup registration also retained that data directory. CLI profile locations were not changed.

The subsequent lifecycle build includes the unfinished-sign-in journal and validated profile cleanup. At 10:28 PM Central it was restarted with the same shortcut arguments, and all four existing account cards refreshed without saved-value, offline, or reconnect errors. No live connection was removed or signed out.

The renewal build adds the shared GitHub operation lease and stale-replacement checks. At 10:33 PM Central, the rebuilt app again showed all four account cards without reconnect or saved-value errors after startup polling.

The CLI-boundary build adds the environment/path checks and bounded Claude runner. At 10:38 PM Central, all four account cards showed refreshed values without saved-value, offline, or reconnect errors.

The profile-location build routes new CLI sign-ins to the local Security root and preserves existing profile references. At 10:45 PM Central, all four existing account cards again showed refreshed values without saved-value, offline, or reconnect errors. At that checkpoint, the shortcut targeted `artifacts/publish/credential-profile-root-win-x64/AIResetRadar.App.exe`, retaining its original arguments. No existing profile or sign-in had yet been moved or replaced. The hashes below identify that historical local package, which was not published.

The packaged ZIP contains 635 entries. SHA-256:

```text
8F3D47890A537442F9D00D3B2E7A8A0CD60324799707AC4A9786D96E3B000785
```

Managed app assembly SHA-256:

```text
D625A4F071D0B08F42EB10499436406F607C23DA16F2FBD2D6615A79B6AFF834
```

A subsequent artifact audit read every ZIP file entry without extracting it and compared its SHA-256 with the corresponding publish file: all 635 matched. The archive hash and entry count also matched the manifest, and the publish-content exclusion check passed. This establishes archive/publish consistency and the configured exclusion checks, not fresh-user installation or live authentication acceptance.

## Codex Reconnect and Cleanup Follow-Up

The user completed one Codex browser reconnect at about 11:12 PM Central on November 2. The replacement account references LocalSecurityDirectory, its snapshot matched the saved identity and scope, and normal usage polling succeeded. Four accounts remain saved, no unfinished sign-in remains, and one retired Codex credential remains queued. SQLite user_version is 4. The new profile exists beneath the local Security namespace and has no auth.json. Root metadata showed current-user ownership and a protected user/SYSTEM DACL. No credential values were read. Absence of auth.json alone does not establish the keyring backend or remote revocation.

The old CLI profile contains roughly 25,000 files. Synchronous validation/deletion made the initial app unresponsive. Cleanup now runs on a worker and awaits completion before clearing its journal. A fixture also reproduced failure on read-only cache files; validated cleanup now handles those attributes without modifying unrelated files. The rebuilt cleanup-cache app was restarted through the existing shortcut arguments and visually checked. At 11:26 PM Central all four accounts had fresh, healthy snapshots, including the replacement Codex connection. The old profile was still pending.

A read-only metadata traversal found no redirected descendants or differing owners among 33,769 entries. With Scrooge stopped to prevent competing cleanup, a diagnostic invoked the production retirement path only for the single retired Codex credential, explicitly rejecting any active credential ID. Its eight-second cancellation deadline elapsed and returned TaskCanceledException after 9.8 seconds. No successful retirement or remote revocation was established.

The fix increases the orchestration deadline to 90 seconds while retaining provider request limits. Automatic approval review initially rejected the longer live retry because it may log out and delete an everyday account's retired profile. The user subsequently approved that operation. The deadline build was started with the original shortcut data-directory arguments, and the shortcut now targets credential-retirement-deadline-win-x64. All four accounts returned healthy, identity-matching snapshots after startup. Native inspection confirmed a responsive dashboard. Local deletion progressed from roughly 25,000 old-profile files to about 5,000 while the account remained queued; the final result is recorded below. This build has not been published.

The approved retry completed. At the 100-second observation, the tracked old profile no longer existed, the retirement queue was empty, and no unfinished sign-in remained. Native inspection showed the pending-cleanup notice had cleared. A subsequent manual Refresh returned healthy, identity-matching snapshots for all four accounts at about 11:39 PM Central. The replacement Codex connection and the other Codex, GitHub, and Claude connections remained usable. The live app is running from credential-retirement-deadline-win-x64 with the original data-directory arguments. This establishes this one local retirement and continued monitoring; it does not establish remote token revocation or the untested lifecycle cases.

The prepared deadline build passed 269 Infrastructure and 118 Core tests and packaged successfully into a 635-file unsigned, self-contained Windows x64 ZIP. Every archive file matched its publish counterpart by SHA-256. The publish-content exclusions passed. These artifact checks are separate from the subsequent live restart and retirement observations. A subsequent isolated --demo launch from this publish directory rendered the dashboard, showed Live monitoring paused, and responded to Refresh. The demo process was then closed. The live database still contained four accounts, zero unfinished sign-ins, and one pending retirement. Demo verification does not exercise logout or removal.

```text
Publish: artifacts/publish/credential-retirement-deadline-win-x64
ZIP SHA-256: BECA0852E2D5B37601E6BCD7C52895AA37BB1EB44D972B0F0B1A34B35CEFF51E
App Assembly SHA-256: 2470A4D1B8D074A532BBC10CAB9CA3FBA764A7E0730DCEA85217E562FB11A413
```

## Synthetic Windows Isolation Probe

The [Credential Isolation Probe](../tools/AIResetRadar.Probe/CredentialIsolationProbe.cs) builds without warnings or errors. It creates a fresh fixture beneath artifacts/native-qa using synthetic data only. The production secret store protects its credential directory. A separate readable copy of the synthetic ciphertext allows a future second-user check to test DPAPI independently of file-access denial.

The owner-account run passed: both protected fixture files were accessible and the copied ciphertext decrypted successfully. Running the different-user mode under that same account returned NotRun with exit code 2, as intended. The dummy plaintext file exercises inherited directory permissions; it does not establish Claude's behavior during sign-in or renewal.

The user confirmed that no second Windows login is available. Actual cross-user denial and fresh-user package verification remain unverified. No additional Windows account was created, and no everyday provider connection was changed for this probe.

## Network Metadata Observation

A 45-second observation ending at 10:49:59 PM Central on November 2 sampled established TCP connections for the running profile-location build and its direct Codex/Claude children:

| Process | Observed Remote Address | Remote Port |
| --- | --- | --- |
| AIResetRadar.App.exe | 140.82.112.5 | 443 |
| codex.exe | 172.64.155.209 | 443 |
| claude.exe | 160.79.104.10 | 443 |

The sample records IP addresses and ports, not verified hostnames or request contents. It did not cover DNS, UDP, detached child processes, or connections too brief to sample. No manual refresh was confirmed during the observation. These results are evidence of observed connections during ordinary operation, not a complete network inventory or proof that no other destinations are contacted. The local metadata report is artifacts/native-qa/credential-network-observation.json.

## Remaining Acceptance Work

### Plan Acceptance Audit

| Plan Requirement | Evidence Available | Acceptance Status |
| --- | --- | --- |
| Windows credential baseline | Installed CLI signature checks, filename/owner inventory, normal polling, synthetic owner DPAPI and permission checks | Incomplete: the matrix covers one Windows/CLI environment and limited lifecycle observations; other versions and second-user checks are missing |
| Secret-store hardening and migration | Storage/lease tests, real DPAPI fixture round trips, interrupted migration tests, observed GitHub migration, explicit CLI location records and version gate | Incomplete: one Codex replacement is healthy at the new root and its old profile was retired; remaining CLI transitions and cross-user denial are unverified |
| Claude supported storage | File-format decision, permission enforcement around CLI execution, successful normal monitoring | Incomplete: live login, renewal/file replacement, two-account isolation, cancellation, logout, and crash recovery are unverified |
| GitHub sign-in review | Official-source decision records the client-secret compatibility blocker and retains device flow | Research decision complete for this scope; no PKCE migration is claimed |
| Retirement and leakage | Durable sign-in journal, persist-before-retire ordering, retry/concurrency tests, synthetic database/error/file leakage tests, publish exclusions, recovery guidance | Incomplete: one authorized Codex retirement completed; other CLI retirement and crash/diagnostic behavior remain open |
| Shipped-result verification | Local native smoke checks, 269 Infrastructure tests, 118 Core tests, package hashes/content comparison, limited TCP metadata sample, updated contributor/security guides | Incomplete: fresh-user exact-package and full live lifecycle checks are missing; no release was published |
| Reporting and recovery preparation | Private-report outline, provider recovery guidance, contribution checklist, AGENTS references | Documentation prepared; channel activation was explicitly outside this plan and was not performed |

The remaining live checks require browser participation and an authorized account/environment for potentially disruptive lifecycle tests. The user completed one Codex reconnect; the rest of the provider lifecycle matrix remains open. The user has no separate provider test accounts or second Windows login. Do not treat those missing prerequisites as passing acceptance, move existing CLI profiles blindly, or exercise logout/revocation on everyday accounts merely to close a checklist.

### Open Checks

- Verify the remaining legacy Codex and Claude transitions; one Codex sign-in, new-location polling, and old-profile retirement succeeded
- Live verification of the implemented unfinished-sign-in journal and CLI profile cleanup; synthetic coverage is complete for the listed cases
- Live isolated sign-in, renewal, logout, two-account Claude isolation, and crash/restart behavior
- Actual access/decryption tests from a second standard Windows user; no second login is currently available
- Fresh-user verification of the exact ZIP and broader network observations across sign-in, renewal, and removal; the limited ordinary-operation sample above is complete
- Additional CLI/enterprise environment compatibility and detached-descendant cleanup verification
- Final release evidence and public claims after the remaining work passes

The user has only everyday provider accounts. Destructive lifecycle tests currently use synthetic data; they must not be presented as live verification. See the [Storage Decisions](decisions/credential-storage.md) for the GitHub PKCE blocker and the supported Claude baseline.
