---
name: Bug report
about: Something does not work as documented
labels: bug
---

**What happened**
A clear description of the problem.

**How to reproduce**
1. `tenon.json` (trimmed to what matters)
2. Command you ran (`tenon build`, `Setup.exe /quiet`, ...)
3. What you expected

**Logs**
Attach the setup log (`Setup.exe /log setup.txt`, or "Open the setup log" on the error page) and the `_msi.log`
file next to it. For build problems, run with `-v` and paste the output.

**Environment**
- Tenon version (`tenon version`):
- Windows version and architecture:
- .NET SDK version (`dotnet --version`):
