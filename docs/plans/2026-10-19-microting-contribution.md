# Microting contribution transition

Decision date: 2026-10-19. Keep this fork as a contribution workspace and target new, scoped changes at [microting/Pomelo.EntityFrameworkCore.MySql](https://github.com/microting/Pomelo.EntityFrameworkCore.MySql). Do not delete or archive the fork while work remains. [ADR 0005](../adr/0005-contribute-incrementally-to-microting.md) records the change in contribution destination.

## Preserved work

- `codex/backup-efcore10-20261019` preserves implementation commit `67fc65ab9cd323a83efd4211c33291fe770ca25c`, including the previously unpushed JSON follow-up. Backup does not imply that the follow-up passed the remote matrix.
- `codex/efcore10-support` preserves planning commit `eb3968b80b4e04a2b0b33676471297d6aa789da6` and the transition documentation added afterward.
- Existing contributions remain in [PomeloFoundation PR #2048](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/pull/2048). PR #2043 was closed in favor of that PR, not merged into official main.
- `origin` remains HoboCY, `upstream` remains PomeloFoundation, and `microting` points to the maintained fork. New contribution branches start from `microting/master` and are pushed to `origin`.

## Initial comparison

Comparison base: Microting `d3548385b356f2dd18e645d78e091664c5c7266f` against the original implementation's 22 commits after `fb2340de`.

| Area | Initial finding | Next action |
| --- | --- | --- |
| Optional-navigation bulk delete | Microting already accepts `LeftJoinExpression`; porting the original fix unchanged would duplicate existing behavior. | Compare remaining shapes and regressions before proposing any follow-up. |
| Parameter-backed primitive collections | Microting does not install the inferred element type-mapping postprocessor added by `5c387077`. | Reproduce through public `DbContext` queries and submit only this mapping fix and its focused tests. |
| Structural JSON | The original implementation contains broader mapping and JSON semantics work, including the unpushed `67fc65ab` follow-up. | Preserve it for a separate comparison; do not mix a large JSON port into the first PR. |
| Migrations, design-time generation and compiled-model baselines | These depend on each fork's EF version and generated artifacts. | Reproduce missing behavior against Microting before moving code or generated baselines. |
| Package consumers, CI gates and skip audit | The original scripts assume the Pomelo package identities, EF 10.0.0 floor and original database matrix. | Adapt separately to agreed Microting policies; do not copy the original release-readiness claims. |
| Performance measurement fixes | Separate from provider correctness and the first contribution. | Keep for an independent follow-up after comparing the target benchmark/test harness. |

This is a migration inventory, not a full provider compatibility certification. Microting 10.0.13 changes the package/assembly/namespace identity and requires EF Core >=10.0.13; the original fork's 10.0.0-floor contract does not transfer automatically.

## First contribution

The first contribution is isolated on `codex/microting-parameterized-collections`, based on Microting master, and pushed at `bc823206a6d71661d915b756d404e5bcfd37b3c3`. It changes three files with 127 added lines: the missing parameter-backed collection type mapping and focused regression tests. The remote SHA was verified after pushing.

- Before the provider fix: both new cases failed on MySQL 8.4.3 with `Expression '@ids' in the SQL tree does not have a type mapping assigned`; neither was skipped.
- After the fix: `AdHocMiscellaneousQueryMySqlTest` passed on MySQL 8.4.3 and MariaDB 11.6.2, each with 73 passed, 0 failed and one pre-existing skip for EF issue #34727. Both new cases passed on both databases.
- Debug and Release builds of the functional test project and its provider/extensions passed with zero warnings and errors. Resolved EF Core version: 10.0.13; SDK: 10.0.100.
- Full platform/database matrix validation and a completed independent review remain outstanding. No maintainer acceptance is claimed.

The PR is prepared but not yet created: the GitHub integration returned HTTP 403 (`Resource not accessible by integration`) for the Microting target, and browser automation was unavailable. Use the [comparison against Microting master](https://github.com/microting/Pomelo.EntityFrameworkCore.MySql/compare/master...HoboCY:codex/microting-parameterized-collections?expand=1) and [prepared PR description](2026-10-19-microting-pr-draft.md) to create a draft after authentication is available. This is a submission blocker, not a code or local-test failure.

The transition and prepared contribution were reported in [issue #12](https://github.com/HoboCY/Pomelo.EntityFrameworkCore.MySql/issues/12#issuecomment-6262590899). Keep that issue open until the first contribution and remaining migration inventory have a recorded outcome.
