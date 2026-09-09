# Pull Request Template

<!--
LedSupBot contribution workflow (see docs/development-workflow.md):

- One task = one branch = one PR.
- Never commit secrets, WhatsApp session data, or local configuration.
- Do not describe features that do not exist in the repository.
- Documentation-only PRs: do not modify product behavior.
-->

## Summary

<!-- What does this PR do, and why? Keep it short and factual. -->

## Scope

- [ ] Backend (ASP.NET Core)
- [ ] Desktop (WPF)
- [ ] Connector (Baileys)
- [ ] Admin (React/Vite)
- [ ] Tests
- [ ] Database / migration
- [ ] Documentation / GitHub files
- [ ] Other: ____

## Changes

<!-- List the concrete changes included in this branch. -->

- ...

## Root Cause

<!-- Only if this PR fixes a bug: what was the root cause? -->

## Tests

- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual verification performed
- [ ] Documentation-only change (no code touched)

Which commands were run?

```powershell
# e.g. dotnet test LedSupBot.sln --configuration Release
#      cd connector && npm test
#      cd admin && npm run test
```

## Build

<!-- CI status or local build result. Do not invent a passing build. -->

## Manual QA

<!-- What did you verify manually? What still requires manual verification?
     Real WhatsApp E2E is manual and must be stated explicitly. -->

## Database / Migration

<!-- Any schema change or migration? Migration content and rollback impact. -->

## Security

- [ ] No secrets, `.env`, session data, or local config committed
- [ ] No WhatsApp linked-device material exposed
- [ ] No API keys in code, commit messages or PR description

## Screenshots

<!-- Add links to real screenshots only. Do not create fake UI assets. -->

## Open Issues

<!-- Anything the reviewer should know or decide. Dependencies on unmerged PRs go here,
     e.g. "Depends on PR #1". -->