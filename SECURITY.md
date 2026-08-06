# Security Policy

## Supported Versions

Healthie.NET ships as a set of NuGet packages — `Healthie.NET` and every
`Healthie.NET.*` published from this repository — which all move in lockstep from
the same tagged release. They are deliberately not listed one by one here: the set
has grown from eight to twenty, and a list in a security policy that lags the
packages is worse than no list, because a reader cannot tell whether their package
is missing or unsupported.

| Version | Supported |
|---|---|
| 4.1.x | :white_check_mark: Supported |
| < 4.1 | :x: End of life |

Only the versions listed above receive security fixes. If you're on an
end-of-life version, please upgrade before reporting an issue — we won't be
able to backport a fix to it.

## Reporting a Vulnerability

**Please do not open a public issue for security reports.**

Report suspected vulnerabilities using GitHub's private vulnerability
reporting, via the Security tab on this repository:

[github.com/ivanvyd/Healthie.NET/security/advisories/new](https://github.com/ivanvyd/Healthie.NET/security/advisories/new)

This opens a private advisory visible only to you and the maintainers, so the
issue can be discussed and fixed before any public disclosure.

### What to include

To help us triage quickly, please include:

- The affected package(s) and version(s) (e.g. `Healthie.NET.Api 2.3.0`)
- A description of the vulnerability and its potential impact
- Steps to reproduce, or a minimal repro project/snippet
- Any relevant configuration (scheduler, state provider, hosting environment)

### Response time

This is a best-effort, maintained-in-spare-time project. We aim to acknowledge
new reports within **7 days** and will keep you updated as the investigation
progresses. Fix timelines depend on severity and complexity.

## Security Considerations for Users

A couple of points worth knowing when you deploy Healthie.NET in your own
application:

- **The management API and dashboard are not authenticated by default.**
  `Healthie.NET.Api` and `Healthie.NET.Dashboard` expose pulse checker state
  and control operations (start, stop, trigger, reset, change interval,
  change threshold) at `/healthie/*`. In production, require authorization —
  both `AddHealthieController(requireAuthorization: true, ...)` and
  `app.MapHealthieUI().RequireAuthorization(...)` support this. Anyone who can
  reach these routes unauthenticated can read your service topology and
  trigger or reset checks.
- **Don't put secrets in pulse checker result messages.** The `Message` on a
  `PulseCheckerResult` is persisted to state history (subject to
  `HealthieOptions.MaxHistoryLength`) via whichever `IStateProvider` you've
  configured, and is surfaced verbatim through both the REST API and the
  dashboard. Avoid including connection strings, credentials, or other
  sensitive data in check result messages or exception details you pass
  through — log them separately instead.
