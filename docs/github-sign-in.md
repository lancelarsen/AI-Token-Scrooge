# GitHub browser sign-in

Requested October 25, 2026. Browser sign-in becomes the default for personal Copilot billing. Manual fine-grained tokens remain an advanced option, including organization billing. The registration initially requests only account Plan read permission; organization Administration access is not silently added.

Radar uses GitHub App device authorization: display GitHub's short user code, open its fixed verification page, and poll at the provider's interval until approval, denial, expiry or cancellation. Request expiring credentials with `offline_access`. A registered app's public client ID is shipped in `github-app.json`; no client secret, private key or server is required. A missing registration produces an explicit setup message.

Trust boundaries are GitHub HTTPS responses, local configuration, the protected credential store, and the account metadata commit. Only fixed GitHub origins are used; redirects are disabled, responses bounded, and provider error text never displayed. Credentials and device codes are never logged. Numeric GitHub identity and billing scope remain authoritative; browser identity alone is insufficient.

Credentials use a versioned DPAPI envelope with originating client ID, expected subject, expirations and renewal state. Existing PAT records remain readable. Renewal and removal share a per-credential lock; the native app already enforces one instance. A renewal intent is stored before exchanging a refresh token. A returned replacement pair is saved before identity verification, because GitHub invalidates the previous pair. Unverified replacements cannot fetch usage. A failed local save retains the replacement in memory for retry; an interrupted exchange or process loss after rotation requires reconnect rather than repeatedly spending the old refresh token. Cancellation after a successful exchange still permits its short local save.

New connections retain the existing staged credential and atomic metadata commit. Reconnect rejects another numeric identity, preserves scope and monitoring preferences, and retires only the replaced credential. Local removal does not revoke the user's GitHub App authorization; the user can revoke it in GitHub settings.

Verification covers polling/backoff, bounded transport, denied/expired/canceled sign-in, concurrent renewal, replacement persistence, transient verification failure, mismatched subjects, removal during renewal, legacy PATs, and native browser handoff. Live acceptance requires app registration and user authorization; fixtures alone do not establish it.

## One-time developer registration

This setup identifies Radar to GitHub; it is not part of every user's sign-in. The developer registers Radar once and ships its public client ID. Users then choose **Sign in with GitHub**, enter the short device code on GitHub, and approve Radar's requested access. They do not need to visit Developer Settings or create a personal access token.

**Only on this account** limits where the GitHub App can be installed and is the initial test setting. **Any account** allows other users or organizations to install it; choosing that option does not automatically connect those accounts or grant access to them. Installation availability and each user's authorization are separate. Broader distribution must retain explicit user authorization and the same narrow read-only permissions.

Register a GitHub App owned by the developer account. Use a recognizable Radar name and the owner's profile as the homepage until a product homepage is available. Enable Device Flow and retain expiring user authorization tokens. Leave callbacks/setup URLs empty and disable webhooks. Grant only Account permissions → Plan → Read-only; leave repository and organization permissions at their defaults. The initial test registration is limited to the owner's account. Review distribution and organization permissions separately before broadening it.

Copy only its public Client ID into `src/AIResetRadar.App/github-app.json`, then publish. Do not generate or bundle a client secret or private key. The JSON is copied beside the executable. Normal users click Sign in with GitHub; they do not register apps or create personal access tokens. The owner completes GitHub's security check and approves registration before any persistent app is created.

Sources: [device authorization](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app), [renewal without a secret for device-flow tokens](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/refreshing-user-access-tokens), [billing permissions](https://docs.github.com/en/rest/billing/usage), [app registration](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app).

## Registration and native checkpoint — October 25, 2026

The owner completed registration for `ai-reset-radar`. Its public Client ID is now configured in `github-app.json`. The registered General page confirmed Device Flow enabled and webhooks disabled. No private key or client secret was generated by Radar's implementation workflow.

Release self-contained x64 publish succeeded with the configured ID. The rebuilt native app opened the browser-sign-in form without the missing-registration warning, using the established graphite surfaces and lavender primary button. The existing OpenAI account remained visible after restart (7% used, 93% left, two earned resets at this observation). GitHub user authorization, billing reconciliation, and live renewal remain pending; registration alone does not prove those checks.

The unsigned preview ZIP was rebuilt with 593 files and .NET runtime 10.0.13. SHA-256: `924C8A4358EE6CDAEAA53DBCEAAAF51D51A8832653222F149055A8B98269B5FB`. Reading `github-app.json` directly from the ZIP confirmed that its nonempty public Client ID exactly matches the source configuration. Packaging now requires this configuration in both the publish folder and archive. This verifies delivery of configuration, not live authorization or clean-user acceptance.
