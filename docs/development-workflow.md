# Development Workflow

> Türkçe özet: Her yeni görev için yeni bir dal açılır (`feature/`, `fix/`, `docs/`, `refactor/`, `test/`).
> Görev tamamlanana kadar test + derleme yapılır, ayrı bir PR açılır, inceleme beklenir ve **yalnızca onay sonrası**
> birleştirilir. Tek görev = tek dal = tek PR. Paralel çalışmalarda ayrı Git worktree'leri kullanılır.

This document defines the permanent development workflow for **LedSupBot**.

## Core Policy

```
EVERY NEW TASK
  → NEW BRANCH
  → IMPLEMENT
  → TEST
  → BUILD
  → COMMIT
  → PUSH
  → PR
  → REVIEW
  → MERGE ONLY WHEN APPROVED
```

Rules that are never optional:

- **One task = one branch = one PR.** Unrelated changes do not belong in the same PR.
- **Never commit secrets.** `.env`, `connector/.env`, WhatsApp session data (`connector/.sessions/`) and
  `appsettings.Local.*.json` are gitignored and must never be committed, pushed or pasted into PR descriptions.
- **Do not advertise unimplemented features.** Documentation must reflect the actual repository state.
- **Do not force-push** shared branches/PRs. Prefer additive commits and `git rebase` only when the branch is local and unpushed.

## 1. Branch Naming

| Area | Branch prefix | Examples |
|---|---|---|
| New capability | `feature/<task>` | `feature/panel-file-delivery` |
| Bug fix | `fix/<bug>` | `fix/bot-runtime-package-delivery` |
| Documentation | `docs/<topic>` | `docs/bilingual-project-readme` |
| Refactor | `refactor/<task>` | `refactor/media-storage` |
| Tests | `test/<task>` | `test/panel-import-cases` |

Choose short, lowercase, dash-separated names that describe the **task**, not the person.

## 2. Before You Start

1. Fetch remote state and determine the **correct base branch**:
   - Usually `master`, or the still-open feature branch your task depends on.
2. Create the branch from that base:

   ```powershell
   git fetch origin --prune
   git switch -c feature/<task> origin/master
   ```

3. If other tasks/agents run in parallel **and** the main worktree is dirty, use a separate worktree:

   ```powershell
   git worktree add -b feature/<task> E:\led-sup-bot-worktrees\<task> <base-branch>
   ```

   A separate worktree keeps your branch isolated and never disturbs uncommitted work of other tasks.

## 3. Implement → Test → Build

- Keep changes scoped to the task. If you discover an unrelated bug, open a **separate** branch/PR.
- For .NET changes: add or update tests, then verify the solution builds and tests pass.
- For connector changes: run `npm test` in `connector/`.
- For admin changes: run `npm run test` and `npm run build` in `admin/`.

## 4. Commit

Write clear, imperative-style commit messages:

```text
feat: add selectable multi-folder package import
fix: require exact customer package key before file delivery
docs: add bilingual project readme
test: prove bulk package persistence end to end
```

- Commit only files belonging to **this** task. Review with `git status --short`, `git diff --stat`,
  `git diff --name-status` and `git diff --check` before committing.

## 5. Push + PR

```powershell
git push -u origin <branch>
```

Open a PR against the correct base branch and follow `.github/pull_request_template.md`.
If your branch depends on an **unmerged** feature branch, state this in the PR description
(e.g. "Depends on PR #1") and set the PR base to that branch.

## 6. Review → Merge

- Do **not** merge your own PR without review (for documentation tasks, the project owner reviews manually).
- Wait for CI (`.github/workflows/ci.yml`) where applicable, and for the requested reviewer's approval.
- Merge only after approval. Use a normal merge or squash merge; do not force-push afterwards.

## Definition of Done

- [ ] Branch created from the correct base with a task-specific name
- [ ] Code/tests/docs changed **only** for this task
- [ ] Tests run (or documented manual QA for documentation-only changes)
- [ ] `git diff --check` clean
- [ ] No secrets, session data or local configuration committed
- [ ] PR opened against the correct base, describing scope, tests and any dependency
- [ ] Reviewed and approved; merged only when approved

## Special Rules for Documentation Tasks

- Documentation-only tasks do **not** modify product behavior, bot logic, the WhatsApp/Baileys runtime,
  the database schema, package delivery behavior or the Desktop UI.
- Real product screenshots may be referenced/copied into `docs/images/`; **never** create fake screenshots
  or fake build badges.
- Unimplemented/planned features must be marked as such (`⚠️ Manual verification required`, etc.).