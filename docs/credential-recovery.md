# Credential Recovery

Use this guide when a connection needs to be restored or a credential may have been exposed. It describes the local implementation and provider documentation reviewed on November 2, 2026. Provider-side revocation has not been tested against the user's everyday accounts.

## Restore a Connection

Open the affected account's settings in Scrooge and use Sign In Again. Sign in to the same provider account and workspace or billing scope. Scrooge verifies the replacement before saving it and retiring the old connection. This preserves the account's settings and history; removing and adding an account is a different operation.

An unreadable credential should be replaced through sign-in, not copied into SQLite or converted to plaintext. Changing the Windows user or moving app data can require reconnecting because the credential store and CLI profiles have their own identities and locations. Keep the old app data until recovery succeeds. Do not copy another application's login files into Scrooge.

## Understand Removal

Turning off Monitor This Account pauses automatic checks and alerts while retaining the connection. Remove Account commits removal in Scrooge, then queues credential cleanup. GitHub cleanup deletes the local saved credential. Codex and Claude cleanup invokes the CLI's logout operation for the selected Scrooge profile before deleting its validated profile directory. Failed cleanup remains queued for retry.

Local removal does not prove that a copied credential is unusable at the provider. A CLI logout success also does not establish the revocation behavior of every supported CLI version. Use the provider controls below for suspected exposure; Scrooge does not automatically revoke shared personal access tokens or all account sessions.

## Respond to Suspected Exposure

Stop using the affected connection while investigating. If the Windows account or computer may be compromised, use a trusted device for provider recovery before signing in again on that computer. Keep tokens, credential files, databases, and raw responses out of support messages. Report a Scrooge defect through the process in [Security](../SECURITY.md#reporting-a-vulnerability).

### GitHub Browser Sign-In

In GitHub Settings, open Applications, then Authorized GitHub Apps. Revoke the affected app authorization. The project's existing registration uses the name AI Reset Radar; confirm the app identity before revoking it. This may affect other connections using that authorization. App authorization and an organization installation are separate controls. See [GitHub's Authorization Guide](https://docs.github.com/en/apps/using-github-apps/reviewing-and-revoking-authorization-of-github-apps).

### GitHub Advanced Token

Delete the affected personal access token in GitHub's Developer Settings under Personal Access Tokens, choosing the matching fine-grained or classic token list. Other tools using that same token will need a replacement. Deleting Scrooge's encrypted copy alone does not revoke it. See [Managing Personal Access Tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#deleting-a-personal-access-token).

### Devin Enterprise

Open **Sign In Again** and complete your company's sign-in for the same Devin user and enterprise. If My Analytics is unavailable, check **Settings** > **Personal** > **My Analytics** in Devin. Scrooge can read only the usage already visible to that user. Removing a connection deletes its Scrooge-owned browser profile through the cleanup queue. For suspected compromise, use your company's sign-in recovery process. Local cleanup does not establish remote session revocation.

### Claude

Claude documents individual Claude Code sign-ins under Settings > Account, or authorization tokens under Settings > Claude Code when they are not listed with sessions. Remove the affected sign-in or token when it can be identified. Claude also documents an account-wide logout that includes Claude Code, but it interrupts other signed-in tools and devices. That action does not revoke API keys. See [Claude's Sign-Out Guide](https://support.claude.com/en/articles/10310342-how-do-i-log-out-of-all-active-sessions).

### OpenAI Codex

Scrooge uses the installed Codex CLI's credential store. OpenAI's [Active Sessions Guide](https://help.openai.com/en/articles/20001257-managing-active-sessions-in-chatgpt) explicitly excludes Codex CLI sessions. Do not treat a missing session row or a ChatGPT browser logout as proof that the CLI credential was revoked.

For suspected Codex credential exposure, contact OpenAI Support for the applicable CLI-session revocation procedure, identifying the product and CLI version without sending the credential. Scrooge's profile logout and removal provide local cleanup; remote revocation of a copied CLI credential remains unverified. Do not extract keyring contents to test whether an exposed token still works.

## Confirm Recovery

After provider recovery, use Sign In Again and verify that the expected account and scope return with a fresh usage reading. A successful new connection proves that monitoring works; it does not independently prove the old credential was revoked. Record the provider's confirmation separately. If Scrooge reports pending cleanup, retain its data and retry after resolving the underlying error instead of deleting its retirement records manually.
