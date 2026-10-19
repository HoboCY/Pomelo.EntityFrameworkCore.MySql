# Issue tracker: GitHub

Issues and PRDs for this working fork live in `HoboCY/Pomelo.EntityFrameworkCore.MySql` GitHub Issues. Use the `gh` CLI for all issue operations and explicitly target the fork. Implementation pull requests are submitted separately to `PomeloFoundation/Pomelo.EntityFrameworkCore.MySql`.

## Conventions

- **Create an issue**: `gh issue create --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --comment "..."`

Always pass `--repo HoboCY/Pomelo.EntityFrameworkCore.MySql` for issue operations. The local `origin` currently points to that fork, but skills must still use the explicit repository name. Use `PomeloFoundation/Pomelo.EntityFrameworkCore.MySql` only for upstream pull-request operations.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --repo PomeloFoundation/Pomelo.EntityFrameworkCore.MySql --comments` and `gh pr diff <number> --repo PomeloFoundation/Pomelo.EntityFrameworkCore.MySql`.
- **List external PRs for triage**: `gh pr list --repo PomeloFoundation/Pomelo.EntityFrameworkCore.MySql --state open --json number,title,body,labels,author,authorAssociation,comments`, then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE`.
- **Comment / label / close**: use `gh pr comment`, `gh pr edit`, or `gh pr close` with `--repo PomeloFoundation/Pomelo.EntityFrameworkCore.MySql`.

The fork's issues and upstream's pull requests use different number spaces. Treat a bare `#42` as `HoboCY/Pomelo.EntityFrameworkCore.MySql#42`; require an explicit `PR #42` or repository name before reading an upstream pull request.

## When a skill says "publish to the issue tracker"

Create an issue in `HoboCY/Pomelo.EntityFrameworkCore.MySql`.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --comments`.

## Wayfinding operations

Used by `/wayfinder`. The map is a single issue with child issues as tickets.

- **Map**: an issue labelled `wayfinder:map`, holding Notes, Decisions-so-far, and Fog.
- **Child ticket**: an issue linked as a GitHub sub-issue. Where sub-issues are unavailable, use a task list and add `Part of #<map>` to the child.
- **Blocking**: use GitHub native issue dependencies. Where unavailable, add `Blocked by: #<n>` to the child.
- **Frontier query**: select the first unassigned open child without an open blocker.
- **Claim**: `gh issue edit <n> --repo HoboCY/Pomelo.EntityFrameworkCore.MySql --add-assignee @me`.
- **Resolve**: comment with the answer, close the issue, then append its context pointer to the map.
