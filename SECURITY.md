# Security policy

Tenon produces installers that run with elevated privileges on other people's machines, so security
reports are taken seriously.

## Reporting a vulnerability

Please do not open a public issue for security problems. Email **liadbenmoshe10@gmail.com** with:

- a description of the issue and its impact,
- steps to reproduce or a proof of concept,
- the Tenon version (`tenon version`) and Windows version.

You will get an acknowledgement within a few days. Once a fix is available a release is published and the
report is credited (unless you prefer otherwise).

## Scope

In scope: the `tenon` tool, the generated MSI and Setup.exe, the custom action DLL, the hook host, and the
`Tenon.Update` and `Tenon.Hooks` packages. Out of scope: vulnerabilities in applications packaged with Tenon,
in Windows Installer itself, or in third-party dependencies (report those upstream, but tell us too).

## Good practices for installer authors

- Sign every output (`signing` section). Unsigned installers trigger SmartScreen warnings.
- Serve update feeds over HTTPS. Downloads are verified against the SHA-256 in the feed.
- Keep hooks small and avoid running untrusted input from the machine inside a hook.
