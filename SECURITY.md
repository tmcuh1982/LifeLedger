# Security policy

LifeLedger stores highly sensitive personal financial information. Security reports are welcome and should be handled privately and responsibly.

## Supported versions

LifeLedger is still under active development. Security fixes are applied to the latest release and the current `main` branch.

| Version | Supported |
| --- | --- |
| Latest release | Yes |
| `main` | Yes |
| Older releases and commits | No |

Users should upgrade to the latest available version before reporting a problem that may already have been fixed.

## Reporting a vulnerability

Do not open a public GitHub issue, discussion or pull request for a suspected vulnerability.

Use the repository's private vulnerability-reporting form from the **Security** tab when it is available. If private reporting is unavailable, open a public issue containing only the sentence "Private security contact requested" and no technical details, personal data or proof of concept. A maintainer will arrange a private communication channel.

Include, when possible:

- the affected version or commit;
- the deployment mode and operating system;
- a concise description of the impact;
- reproducible steps using synthetic data;
- relevant logs with all personal information removed;
- any proposed mitigation.

Never attach a real LifeLedger database, backup, CSV bank export, address, account identifier, portfolio, access token, log file containing financial data, or other personal information. Build a minimal reproduction with fictional data instead.

## Response process

The maintainers aim to:

- acknowledge a report within seven days;
- confirm whether the issue is reproducible and in scope;
- provide a progress update within 30 days;
- coordinate a fix and public disclosure when appropriate;
- credit the reporter if requested and safe to do so.

These are best-effort targets for a volunteer open-source project, not guaranteed service levels.

## Issues considered in scope

Examples include:

- unauthorized access to local financial data;
- exposure of data through logs, exports, backups or external integrations;
- injection, cross-site scripting, path traversal or unsafe file handling;
- destructive operations that bypass required confirmation or validation;
- vulnerabilities in CSV or JSON import processing;
- external requests that disclose more information than the user explicitly enabled;
- unsafe plugin loading or execution;
- dependency vulnerabilities that are exploitable in LifeLedger.

Calculation disagreements, inaccurate financial assumptions and feature requests are not security vulnerabilities unless they also allow unauthorized access, data corruption or code execution.

## Coordinated disclosure

Please allow reasonable time for investigation and remediation before publishing technical details. Do not access data that is not yours, degrade another person's installation, use social engineering, or retain sensitive information discovered during testing.

Good-faith research that follows this policy will be treated as an effort to improve LifeLedger's security.

## Security principles

LifeLedger is designed to remain local-first and privacy-first:

- personal financial data stays in the self-hosted database;
- outbound market-data or currency requests must be explicit and limited;
- secrets, local databases, backups and logs must never be committed;
- imports and migrations must preserve data integrity and fail safely;
- integrations must disclose which data leaves the local installation.
