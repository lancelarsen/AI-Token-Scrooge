# App Versions and Updates

Settings shows the installed version above Time Zone. Versions use `vYYYY.MM.D.NNN`, such as `v2026.11.3.001`. The date is Central Time; the final number advances for each build reserved that day. Local builds use revision `000` and are labeled Development Build.

## Checking and Installing

- **Check for Newer Versions:** On by default. Check at startup when due and every eight hours while Scrooge runs, including in the tray. The saved last successful check survives restarts. A failed automatic check waits at least 30 minutes before retrying.
- **Check Now:** Check GitHub immediately, even when automatic checks are off.
- **Get Update:** Download a newer Windows x64 package and verify it while the current app keeps running.
- **Update and Restart:** Prepare the helper, wait for Scrooge to exit, activate the new version, and reopen the dashboard with the same data directory.

There is no rollback feature. A failed download or package validation does not change the active version. An activation or launch failure is reported; the user can extract a fresh release into another folder. Accounts, credentials, settings, and history are not part of the update package and are not migrated by the updater.

## Release Versions

`New-BuildVersion.ps1` lists the day's `vYYYY.MM.D.NNN` tags and reserves the next number before building. The workflow serializes publication. Failed builds can leave gaps; their tags remain reserved. Deleting an old downloadable release must not delete its calendar-version tag or reset the counter. Old `preview-N-N` tags can still be removed.

The build embeds the numeric version in the app assembly and includes `build-info.json` with the display version, commit, and update protocol. The ZIP and manifest include the version in their filenames. The release title remains AI Token Scrooge for Windows (x64). Cleanup runs only after upload and requires a non-empty ZIP on the newest release.

## Network and Trust

The updater reads the public [GitHub Releases API](https://docs.github.com/en/rest/releases/releases) for `lancelarsen/AI-Token-Scrooge`. It includes prereleases rather than relying on the latest-release endpoint. No provider token, account identifier, prompt, history, or database is sent. GitHub receives the ordinary connection metadata and Scrooge's product User-Agent.

Only the exact versioned Windows x64 ZIP from this repository is eligible. Downloads use HTTPS and allow redirects only to GitHub's release-asset hosts. Scrooge checks the exact size and [GitHub-provided SHA-256 asset digest](https://docs.github.com/en/rest/releases/assets), then validates the ZIP paths, required files, embedded version, and update protocol. Metadata, compressed size, expanded size, file count, redirects, and request time are bounded.

These previews are unsigned. SHA-256 checks the downloaded bytes against metadata obtained from GitHub over HTTPS; it is not a publisher signature and does not protect against compromise of the repository or GitHub account. No update request uses GitHub provider credentials. A signed installer remains a separate roadmap item.

## Installation Layout

The portable folder contains the original app and `Launcher/Scrooge.exe`. The launcher is a [self-contained .NET single-file executable](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), so it does not require an installed .NET runtime.

- `Updates/<random-id>` holds a downloaded ZIP, checked files, and a handoff containing the parent process identity and restart arguments. It contains no provider credentials.
- `Versions/<version>` holds the activated application files.
- `current-version.txt` selects the active version. A temporary pointer is replaced after staging succeeds.
- `update.lock` coordinates the stable launcher and update helper.

The original executable forwards subsequent launches to the selected version, preserving existing shortcuts. Windows startup uses the stable launcher when present. The original bootstrap files remain in place because shortcuts may refer to them; they are not offered as a rollback version. The helper removes the previous version directory after launching the new one. Locked files or directories containing app data are preserved rather than forcibly removed.

Paths must be ordinary directories without reparse points. ZIP entries cannot escape staging, duplicate a path, create a link, or contain app data. Cleanup is restricted to updater-owned staging and version directories. The helper checks the parent PID, start time, and executable path, verifies the ZIP again before signaling readiness, and waits for a normal exit. It does not elevate or kill the monitoring process. Scrooge must have write access to its portable folder.

## Verification

Regression tests cover numeric ordering, invalid versions, old preference defaults, the check schedule, release selection, digest and size mismatch, redirect restrictions, unsafe and duplicate ZIP paths, incomplete packages, version/protocol mismatch, activation, and preserving local data.

Native acceptance must separately exercise Settings at desktop and compact sizes, save/reopen of the check preference, an actual download, restart handoff, preserved data-directory arguments, reopening through the original executable, and the published workflow. Unit tests alone do not establish those results.
