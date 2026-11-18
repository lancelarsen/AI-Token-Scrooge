# Chrome API Credit Tracking

Scrooge's Chrome extension reads the full OpenAI API organization credit pool using the account already signed in to Chrome. It replaces the experimental embedded sign-in window. The extension and Windows bridge are a local preview, not a Chrome Web Store release. Initial live connection and balance display were confirmed on November 17, 2026; refresh and restart acceptance remain open.

## Build and Install

Use the repository's pinned .NET SDK and Windows requirements. Publish the app as described in [Testing](testing.md), then publish the bridge:

```powershell
dotnet publish tools/AIResetRadar.ChromeHost/AIResetRadar.ChromeHost.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/chrome-host-win-x64
./scripts/Install-ChromeBridge.ps1 -HostDirectory artifacts/publish/chrome-host-win-x64
```

The installer copies the bridge to the current user's local Programs directory and registers only `com.aitokenscrooge.billing` under the current user's Chrome native-messaging registry key. No administrator access is needed. The bridge must be installed separately from the ordinary app package in this preview.

In Chrome, open `chrome://extensions`, enable Developer Mode, choose **Load Unpacked**, and select `extensions/scrooge-api-credits` from this checkout. Keep that folder available. The manifest's public key fixes the extension ID so only that extension origin is allowed by the native host. The public key is not an authentication secret or a publisher signature. Do not change it without updating the host identity and registration.

## Connect an Organization

The connection panel's **Install the Chrome Extension** button expands setup instructions. **Copy Extension Folder Path** copies the bundled `ChromeExtension` directory, ready to paste into Chrome's **Load Unpacked** folder picker. The source-checkout directory above remains valid for developers. Keep whichever folder you loaded in place. For an existing installation, use its **Reload** button on `chrome://extensions` after an update.

Pin **Scrooge API Credits** from Chrome's puzzle-piece Extensions menu. Its purple Token Baron top-hat icon then appears beside the address bar. After Chrome opens the billing page, click that icon and choose **Track This Organization** in the popup. Leave Scrooge's connection panel open; it closes automatically after confirmation. **Cancel** stops the connection rather than finishing it.

Open Scrooge and Chrome. In Scrooge, choose **Add Account**, **OpenAI API**, then **Connect with Chrome**. Chrome opens the credit-grants page using its normal login. If needed, sign in and select the intended organization yourself. Open the **Scrooge API Credits** extension from Chrome's extensions menu, review the balance and account/organization identifiers, and choose **Track This Organization**.

Scrooge accepts the connection only after a second page load returns the same account and organization. The subscription benefit amount is not used as a balance. See [API Credit Research](providers/openai-api-credits.md) for the source and dollar semantics.

## Refresh and Failure Behavior

Keep Chrome running with the extension enabled. Each scheduled Scrooge read opens a temporary inactive billing tab, observes fresh responses, and closes that tab after completion or cancellation. Interactive sign-in tabs remain available to the user. Scrooge does not switch the selected API organization. If the Chrome session's selected organization or user differs from the connected account, the reading is rejected and Scrooge asks you to select the original organization.

The extension reconnects its native host on a 30-second alarm after Scrooge restarts. An immediate refresh during reconnection can report Chrome unavailable; retry after the extension reconnects. Closed Chrome, sign-out, a changed provider response, missing billing permission, or an interrupted bridge leaves the previous Scrooge reading stale or unavailable. It does not become a new zero balance. Provider rate limits retain a bounded retry delay.

This preview supports one connected Chrome profile at a time. Multiple simultaneous Chrome profiles and different API organizations require further live testing. Requests use a fresh ID and document identity; canceled or previous-process responses cannot satisfy a new read. Account removal and reconnect still use Scrooge's existing persistence and stale-result protections.

## Data and Permissions

The extension requests access only to `https://platform.openai.com/*`, native messaging, alarms, and session storage. It does not request cookies, debugger access, Google account-page access, or general browsing-history permission. It watches the Platform page's own login and credit-grant responses and sends only normalized account/organization IDs, dollar totals, grant IDs, remaining amounts, and expirations. No provider requests are issued directly by the extension.

The page observer runs in the Platform page's JavaScript context. That page can influence what the observer sees, so its output is treated as untrusted input, validated in the extension worker and again in Scrooge. This design trusts the signed-in Platform page as the billing source; it does not provide cryptographic attestation of provider data. Browser login tokens, passwords, raw responses, and request authorization headers are not sent to Scrooge or saved by the extension. A compromised Platform page or same-user local process is outside this trust boundary.

Chrome starts the installed host over standard input/output. The host checks the extension origin and forwards bounded messages to Scrooge over a current-user-only Windows named pipe. There is no listening HTTP port. The manifest and origin check do not protect against malicious programs already running as the same Windows user. Normalized readings enter Scrooge's existing account/history storage; the extension keeps request/tab bookkeeping only in session storage.

## Remove the Connection

Remove the API account in Scrooge to stop its requests. Existing journal and cleanup code removes only Scrooge's owned connection directory, including any abandoned profile from the old embedded-browser experiment. It does not sign out Chrome, delete Chrome's cookies, or revoke your OpenAI account session. Remove the extension in Chrome and run `scripts/Unregister-ChromeBridge.ps1` to unregister the host. The unregister script leaves installed binaries in place; it does not delete unrelated files.

## Verification

Run `node --test extensions/scrooge-api-credits/tests/*.test.js` and the .NET solution tests. Fixtures cover response normalization, exclusion of synthetic secrets, scope validation, explicit confirmation followed by reload, old-document rejection, cancellation, malformed/oversized framing, and current-request correlation. Fixture success does not establish live Chrome or provider compatibility.

User-provided screenshots on November 17, 2026 confirmed the extension read $115.99 remaining and the connected Scrooge row displayed the same balance and November 29 expiration. This establishes initial connection only. Before release, refresh with Chrome open, restart both apps, close Chrome, change the selected organization, remove/reconnect the account, and verify compact/native rendering.

References: [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [Content Scripts](https://developer.chrome.com/docs/extensions/reference/manifest/content-scripts), and [Service Worker Lifecycle](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle).
