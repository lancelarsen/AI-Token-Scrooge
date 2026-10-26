# GitHub browser sign-in

Requested October 25, 2026. Browser sign-in becomes the default for personal Copilot billing. Manual fine-grained tokens remain an advanced option, including organization billing. The registration initially requests only account Plan read permission; organization Administration access is not silently added.

Radar uses GitHub App device authorization: display GitHub's short user code, open its fixed verification page, and poll at the provider's interval until approval, denial, expiry or cancellation. Request expiring credentials with `offline_access`. A registered app's public client ID is shipped in `github-app.json`; no client secret, private key or server is required. A missing registration produces an explicit setup message.

Trust boundaries are GitHub HTTPS responses, local configuration, the protected credential store, and the account metadata commit. Only fixed GitHub origins are used; redirects are disabled, responses bounded, and provider error text never displayed. Credentials and device codes are never logged. Numeric GitHub identity and billing scope remain authoritative; browser identity alone is insufficient.

Credentials use a versioned DPAPI envelope with originating client ID, expected subject, expirations and renewal state. Existing PAT records remain readable. Renewal and removal share a per-credential lock; the native app already enforces one instance. A renewal intent is stored before exchanging a refresh token. A returned replacement pair is saved before identity verification, because GitHub invalidates the previous pair. Unverified replacements cannot fetch usage. A failed local save retains the replacement in memory for retry; an interrupted exchange or process loss after rotation requires reconnect rather than repeatedly spending the old refresh token. Cancellation after a successful exchange still permits its short local save.

New connections retain the existing staged credential and atomic metadata commit. Reconnect rejects another numeric identity, preserves scope and monitoring preferences, and retires only the replaced credential. Local removal does not revoke the user's GitHub App authorization; the user can revoke it in GitHub settings.

Verification covers polling/backoff, bounded transport, denied/expired/canceled sign-in, concurrent renewal, replacement persistence, transient verification failure, mismatched subjects, removal during renewal, legacy PATs, and native browser handoff. Live acceptance requires app registration and user authorization; fixtures alone do not establish it.

## One-time developer registration

Register a GitHub App owned by the developer account. Use a recognizable Radar name and the owner's profile as the homepage until a product homepage is available. Enable Device Flow and retain expiring user authorization tokens. Leave callbacks/setup URLs empty and disable webhooks. Grant only Account permissions → Plan → Read-only; leave repository and organization permissions at their defaults. The initial test registration is limited to the owner's account. Review distribution and organization permissions separately before broadening it.

Copy only its public Client ID into `src/AIResetRadar.App/github-app.json`, then publish. Do not generate or bundle a client secret or private key. The JSON is copied beside the executable. Normal users click Sign in with GitHub; they do not register apps or create personal access tokens. The owner completes GitHub's security check and approves registration before any persistent app is created.

Sources: [device authorization](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app), [renewal without a secret for device-flow tokens](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/refreshing-user-access-tokens), [billing permissions](https://docs.github.com/en/rest/billing/usage), [app registration](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app).
