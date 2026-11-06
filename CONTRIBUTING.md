# Contributing to AI Token Scrooge

Contributions are welcome: new provider integrations, bug fixes, and Windows packaging work. Grok and other services are good places to help if you can establish a reliable, read-only usage source.

Start with the [Contribution Roadmap](tasks/contributor-roadmap.md). For a new provider or a substantial design/architecture change, open an issue describing the problem, proposed approach, and evidence before investing in a large PR. Small fixes can go straight to a focused PR.

## Read Before Changing Code

| Work | Required Guide |
| --- | --- |
| Every Contribution | [Agent and Repository Instructions](AGENTS.md), [Architecture](docs/architecture.md), [Testing](docs/testing.md) |
| UI, Copy, or Interaction | [Design Guidelines](docs/design.md) |
| Provider, Sign-In, Credentials, or Polling | [Provider Integration](docs/integration.md), [Security](SECURITY.md) |
| Storage or Migrations | [History Storage](docs/history-storage.md), [Architecture](docs/architecture.md#persistence-and-secrets) |
| Packaging or Release | [Release Acceptance](docs/release-acceptance.md), [Windows Workflow](.github/workflows/windows-preview.yml) |

The current guides describe required behavior and current boundaries. Older design explorations and dated verification notes provide context; they do not override current requirements or prove that a new change works.

## Development Setup

Use Windows and the SDK pinned in `global.json`. Follow [Build and Run from Source](README.md#build-and-run-from-source), then run the tests in [Testing](docs/testing.md). Use an isolated `--data-dir` and `--demo` for visual work. Do not experiment against another person's credentials or overwrite your normal app data.

Inspect `git status` before editing. Preserve unrelated changes and stage only your contribution. The main branch may differ from a contributor's locally modified preview; identify the commit and build you tested.

## A Good Pull Request

Explain the user-visible problem and resulting behavior. Include the affected provider/account type if applicable, relevant tests and build results, and what remains unverified. For UI work, name the existing control or page used as the reference and include redacted desktop and compact screenshots from the rebuilt app.

Keep changes focused. Follow the existing project boundaries and control library. Do not add a framework, provider registry, alternate theme, or new settings persistence mechanism merely to support one feature. Discuss a larger refactor separately when it solves an evidenced problem.

Use Title Case for headings and short labels; use sentence case for explanatory prose. Preserve the product name and tagline. Update docs and TODOs with the implementation, but do not check off live acceptance based only on mocked tests.

## Provider Contributions

Start with the research note and verification matrix in [Provider Integration](docs/integration.md). Prefer provider browser sign-in or the installed official CLI. A new provider must reuse Scrooge's account cards, timeline, alert rules, credential isolation, and cleanup flow. It must not consume model tokens to discover usage.

For Devin Enterprise, use personally authorized My Analytics access. Keep tenant names, identity values, and live browser profiles out of the repository.

Complete the [Credential Checklist](docs/integration.md#credential-checklist) and include the provider's [Recovery Guidance](docs/credential-recovery.md). Fixture tests do not establish live renewal, Windows cross-user isolation, or remote revocation.

## Issues and Security

For ordinary bugs, include the app version or commit, Windows version, provider, reproduction steps, expected/actual behavior, and sanitized screenshots. Never attach your database, credential files, browser cookies, tokens, or raw provider responses.

Follow [Security](SECURITY.md) for vulnerabilities and provider-specific credential storage requirements. Planned hardening and sign-in research are tracked in the [Credential Protection Plan](tasks/credential-protection-plan.md); proposed protections must not be described as shipped. Be respectful, make feedback specific, and explain tradeoffs without personal attacks.

## Licensing and Maintainer Decisions

AI Token Scrooge is open source under the [MIT License](LICENSE). Contributions are provided under the same license. Preserve third-party notices. Identify the source and license of any new icons, sounds, or other assets.

## Release Behavior

Pushes to main run the Windows preview workflow: install the pinned toolchain, test, publish, package, and upload an unsigned prerelease ZIP and manifest. Local commits and unmerged PRs do not publish releases. Contributors should verify the behavior their change affects using the relevant testing guide.

Calendar-version tags reserve daily build numbers permanently, even after older downloadable releases are removed. Read [App Versions and Updates](docs/app-updates.md) before changing version allocation, package layout, release filtering, or the restart helper. Do not delete reserved version tags or treat a checksum as a publisher signature.
