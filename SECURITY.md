# Security Policy

`dzl` (DayZ Labs) is a local Windows developer tool. It launches processes, mounts the
**P:** work drive, manages your mod-signing keys, and can hold Steam and GitHub
credentials for optional features. We take the safety of that surface seriously.

## Supported versions

`dzl` is pre-1.0 and ships as a single auto-updating app. Only the **latest release**
receives security fixes — the tray checks for updates on launch, so staying current is
the supported path.

| Version        | Supported |
| -------------- | --------- |
| Latest release | ✅        |
| Older releases | ❌        |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through GitHub's
[**Report a vulnerability**](https://github.com/Borcioo/dayz-labs/security/advisories/new)
(repo **Security → Advisories**). Include:

- what the issue is and how to reproduce it,
- the `dzl` version (tray **About** tab) and your Windows version,
- the impact you think it has.

You'll get an acknowledgement, and we'll work with you on a fix and coordinated
disclosure. Please give us reasonable time to ship a patch before disclosing publicly.

## About the unsigned installer

The installer is currently **unsigned**, so Windows SmartScreen shows an
"unknown publisher" prompt on first run. Every release is **scanned on VirusTotal**
(link in the release notes) so you can verify the binary independently. Always download
from the [official releases page](https://github.com/Borcioo/dayz-labs/releases) — never
from a mirror.
