# Contribute incrementally to Microting

Accepted on 2026-10-19 after reviewing fork issue #12. Keep the HoboCY fork as a contribution workspace and preserve the existing EF Core 10 implementation and planning history. New, focused contributions target `microting/Pomelo.EntityFrameworkCore.MySql:master` through branches in the HoboCY fork. Keep `origin` pointing to HoboCY, `upstream` pointing to PomeloFoundation, and add `microting` for the new contribution base.

This supersedes ADR 0001's contribution destination for new work. PomeloFoundation PR #2043 was closed in favor of PR #2048, which includes previous contributions; preserve that PR. Compare each existing fix against Microting before porting it, including current code and tests rather than relying on issue or PR state. Keep unrelated fixes and generated baselines out of each contribution.

ADRs 0002-0004 remain the historical contract for the original Pomelo upgrade. They do not assert that Microting supports EF Core 10.0.0: its current 10.0.13 package requires EF Core >=10.0.13. Use the target project's existing dependency, namespace and database policies for focused fixes. Any proposal to change those policies needs separate agreement. Validate regressions before and after each fix on the relevant databases; a scoped PR is not a claim of full release readiness.

Do not delete or archive the fork while contributions or unported work remain. Do not publish packages, create release tags, change repository ownership, or migrate downstream applications as part of this transition.
