# Native EF Core 10 support with release-grade MySQL and MariaDB validation

## Problem Statement

Pomelo's current mainline release is a native Entity Framework Core 9 provider. Applications can run Pomelo 9 and EF Core 9 on .NET 10, but they cannot consume the EF Core 10 provider contract or rely on EF Core 10 provider behavior. The open EF Core 10 upgrade work in PR #2043 carries most of the port, but it has not yet established the release-grade contract required for an upstream contribution: the complete compatibility range is not continuously proven, deferred EF Core 10 specification suites need an explicit audit, the full MySQL/MariaDB matrix must remain authoritative, and a known optional-navigation `ExecuteDelete` regression needs a public-behavior test and fix.

Without a completed native provider, downstream .NET 10 applications that require EF Core 10 features must stay on EF Core 9, use an unofficial fork, change providers, or accept unverified behavior. Contributors also lack a bounded definition of what Pomelo 10.0.0 must preserve, what new EF Core 10 capabilities may be deferred, and what evidence is required before the work is ready for maintainers.

## Solution

Complete a compatibility-first, native EF Core 10 provider on top of PR #2043. Preserve the public capabilities of Pomelo 9, adapt the provider runtime, query pipeline, migrations, design-time services, relational behavior, and structural JSON mapping to EF Core 10, and declare support for the complete EF Core `10.0.x` patch line.

Compile against EF Core `10.0.0` as the compatibility floor while adding a separately pinned validation lane for the latest stable EF Core 10 patch. Validate behavior through the public `UseMySql` and `DbContext` surface against real MySQL and MariaDB servers, through real `dotnet ef` design-time commands, and through package-consumer tests at both ends of the supported patch range.

Treat release readiness as an evidence gate. Debug and Release builds, packaging, unit tests, functional tests, migrations, scaffolding, integration tests, the existing Linux/Windows database matrix, the known `ExecuteDelete` regression, and the audited skip policy must all pass without unexpected failures. Produce small, independently reviewable commits on top of PR #2043 and contribute them to that upgrade line while it remains open, or rebase them onto official main after it merges.

## User Stories

1. As a .NET 10 application developer, I want a Pomelo provider compiled for EF Core 10, so that I can adopt EF Core 10 without replacing MySQL or MariaDB.
2. As an existing Pomelo 9 user, I want my current public provider behavior preserved, so that upgrading does not introduce silent query or persistence regressions.
3. As an EF Core 10 consumer, I want all Microsoft EF Core packages in my application to use a compatible 10.0 patch, so that dependency resolution remains coherent.
4. As a library author, I want Pomelo 10 to support the complete EF Core `10.0.x` patch line, so that my library does not force consumers onto one arbitrary patch.
5. As a conservative consumer, I want the provider compiled against EF Core `10.0.0`, so that later patch APIs do not accidentally become the minimum requirement.
6. As a security-conscious consumer, I want the provider continuously tested against the latest stable EF Core 10 patch, so that patch updates do not remain unverified.
7. As a MySQL 8.4 operator, I want provider queries and updates tested against my server family, so that generated SQL is valid in production.
8. As a MySQL 8.0 operator, I want the supported compatibility line retained, so that upgrading EF Core does not implicitly drop my database version.
9. As a MariaDB operator, I want provider behavior tested on every existing supported MariaDB line, so that MySQL-specific SQL does not leak into MariaDB execution paths.
10. As a cross-platform developer, I want the provider built and tested on Linux and Windows, so that platform-specific tooling or path behavior is detected before release.
11. As an application developer using LINQ, I want EF Core 10 query translation to preserve Pomelo semantics, so that equivalent queries continue returning equivalent results.
12. As an application developer using bulk operations, I want `ExecuteUpdate` and `ExecuteDelete` to generate legal MySQL/MariaDB SQL, so that server-specific restrictions do not surface at runtime.
13. As a developer deleting through an optional navigation, I want `ExecuteDelete` to retain a valid multi-table delete shape, so that MySQL error 1093 is not introduced by the EF Core 10 port.
14. As a developer using nested `Skip` and `Take`, I want limit and offset expressions translated into server-supported SQL, so that EF Core 10 query rewrites remain executable.
15. As a developer using complex or owned types stored as JSON, I want provider-specific structural JSON type mapping, so that EF Core 10 can materialize, parameterize, update, and migrate those models.
16. As a developer using spatial data, primitive collections, string comparisons, temporal values, and numeric translations, I want provider baselines verified per database family, so that differences are intentional rather than accidental.
17. As a developer using migrations, I want migration generation and execution tested through public EF APIs and actual databases, so that schema changes remain deployable.
18. As a developer using idempotent or legacy migration flows, I want their existing provider behavior preserved or explicitly documented, so that upgrade risks are visible.
19. As a developer reverse-engineering an existing database, I want `dotnet ef dbcontext scaffold` to succeed with Pomelo 10, so that design-time support remains complete.
20. As a developer consuming generated models, I want compiled-model and annotation generation behavior reconciled with EF Core 10, so that design-time output compiles and runs.
21. As a maintainer, I want every skipped EF Core 10 test family classified, so that a green run cannot hide an unported provider capability.
22. As a maintainer, I want skips limited to documented server limitations or explicitly deferred features, so that support claims remain honest.
23. As a maintainer, I want deferred EF Core 10 feature families traceable to follow-up work, so that compatibility-first does not become permanent ambiguity.
24. As a reviewer, I want changes split into focused commits on top of PR #2043, so that fixes can be reviewed or cherry-picked independently.
25. As a reviewer, I want tests to exercise public provider behavior rather than internal visitor call counts, so that refactoring does not invalidate useful coverage.
26. As a release maintainer, I want local package artifacts for compatibility testing without automatic publication, so that validation cannot accidentally become a release.
27. As a release maintainer, I want final preview, RC, and RTM branding left under maintainer control, so that a contributor does not claim release authority.
28. As a contributor, I want the implementation isolated from local planning documents, so that upstream-ready diffs contain only product code, tests, CI, and necessary product documentation.
29. As a contributor, I want fork CI to run the complete remote matrix after local gates pass, so that Windows and database combinations unavailable locally are still verified.
30. As a downstream maintainer, I want the produced package restored and executed in applications pinned to the compatibility floor and latest patch, so that declared dependency ranges are proven from a consumer perspective.

## Implementation Decisions

- Use the latest verified head of upstream PR #2043, currently commit `fb2340de94736bc3d33830c25d7602f6e8fa5065`, as the technical baseline instead of repeating the upgrade from main or using the older PR #2019 implementation.
- Keep planning and decision documents separate from the upstream-ready implementation branch. The implementation branch contains only provider code, tests, CI changes, dependency updates, and necessary product documentation.
- Preserve PR #2043's history as the baseline and add focused commits that can be reviewed and cherry-picked independently.
- While PR #2043 remains open, target incremental fixes at that upgrade line. If it merges first, rebase the incremental commits onto official main. Do not open a competing full-upgrade PR unless maintainers request one.
- Target .NET 10 for provider projects, test projects, tooling, and design-time execution where EF Core 10 requires it.
- Declare the EF Core dependency range as `[10.0.0,10.0.999]`.
- Compile the normal provider build against EF Core `10.0.0` to preserve the minimum compatibility contract.
- Maintain a separately pinned validation lane for the latest stable EF Core 10 patch, currently `10.0.11`, and advance that pin when newer stable patches are released.
- Upgrade the complete compatible dependency and tooling slice rather than changing only target frameworks or EF Core packages. This includes Microsoft.Extensions and ASP.NET Core dependencies, design-time tooling, Roslyn dependencies, test infrastructure, and packages required by provider extensions.
- Preserve `MySqlConnector` as the database driver and retain the existing package extension set for core, Microsoft JSON, Newtonsoft JSON, and NetTopologySuite support.
- Preserve the public `UseMySql` configuration surface and existing provider option semantics unless an EF Core 10 API break makes an additive or source-compatible adjustment unavoidable.
- Adapt the EF Core parameter-based SQL processing pipeline to the EF Core 10 contract while preserving parameter correctness and SQL cache safety.
- Ensure parameter lookup uses EF Core 10 invariant parameter identity rather than display names when reading decorated parameter values.
- Retain server-valid handling for nested `Skip`/`Take` limit and offset expressions, including disabling SQL caching when generated SQL depends on runtime parameter values.
- Adapt query-compilation factories, relational processing, SQL visitors, bulk update/delete validation, and precompiled-query integration points required by EF Core 10.
- Fix optional-navigation `ExecuteDelete` through the public query seam. Preserve deliberate checks that reject unsupported delete shapes while allowing legal left-join multi-table deletes rather than falling back to a MySQL-invalid self-referencing subquery.
- Preserve and validate provider-specific structural JSON mapping for EF Core 10 complex types and owned entities, including reading, parameter binding, SQL literals, updates, migrations, and provider store-type selection.
- Reconcile migrations, model differ behavior, history repository behavior, generated annotations, compiled models, and reverse engineering with EF Core 10 design-time contracts.
- Keep the release compatibility-first. Existing Pomelo 9 public capabilities must remain production-ready on EF Core 10. New EF Core 10 feature families may be deferred when they are outside this initial contract.
- Every deferred feature family must be explicit and traceable. Database-native limitations may use narrowly justified skips. Blanket ignored specification suites cannot be used to imply that unported functionality is supported.
- Retain the existing Linux/Windows and MySQL/MariaDB CI matrix unless maintainers separately approve a support-policy change.
- Add a consumer-level compatibility validation for the floor and latest-patch EF Core versions rather than relying only on the provider solution's internal package graph.
- Produce local NuGet packages for validation only. Do not create release tags, publish GitHub Releases, or upload packages to NuGet/MyGet.
- Do not change final preview, RC, or RTM branding as part of incremental fixes unless a Pomelo maintainer requests it.

## Testing Decisions

- Test external provider behavior at the highest practical seam. The primary seam is a real `DbContext` configured through public `UseMySql` APIs and executed against actual MySQL or MariaDB.
- Reuse EF Core relational specification tests and existing Pomelo functional fixtures as the principal behavioral test foundation.
- Assert observable results, generated SQL when SQL shape is part of the provider contract, database state, exceptions, and public diagnostics. Do not test private visitors, private helper methods, or internal call counts.
- Add a focused regression test for `ExecuteDelete` through an optional reference navigation. It must execute against MySQL and prove the generated operation succeeds without error 1093.
- Cover both nullable and non-nullable relationship variants where they select different join shapes.
- Retain tests for legal and illegal bulk-delete shapes so the fix does not broadly relax safety checks around limits, offsets, non-table roots, or unsupported joins.
- Validate structural JSON behavior through model configuration, persistence, querying, updating, migrations, and round trips rather than by instantiating internal mappings directly.
- Validate query translations on both MySQL and MariaDB whenever their SQL grammar or behavior differs.
- Preserve server-aware expected SQL for known MySQL/MariaDB differences instead of blessing one server's baseline for every server.
- Run Debug and Release solution builds and treat warnings as errors under the repository's existing policy.
- Run the complete unit suite, functional suite, migration commands, scaffolding commands, legacy migration flow, integration suite, and package build.
- Run the existing Linux and Windows CI jobs across both MySQL lines and all existing MariaDB lines. Any failure not explicitly classified as an existing infrastructure issue blocks completion.
- Audit all EF Core 10 additions to ignored test bases. Each retained skip must identify a server limitation, upstream EF issue, already-existing unsupported capability, or explicit follow-up item.
- Reject broad skips whose only justification is that a new EF Core 10 suite has not yet been ported.
- Build and consume local packages in two external test applications: one pinned to EF Core `10.0.0`, and one pinned to the latest stable EF Core 10 patch.
- Verify restore resolution, application compilation, provider registration, database connection, model creation, a representative query, an insert/update/delete round trip, and a migration operation in both package-consumer applications.
- Keep the latest-patch lane explicitly pinned for reproducibility; update it deliberately when a new stable patch is adopted.
- Use local .NET 10 and Docker validation before pushing. After local gates pass, push only the implementation branch to the user's fork so GitHub Actions can exercise the full remote matrix.
- A green build with unexamined skips is not sufficient evidence. Completion requires a skip audit and a written accounting of every EF Core 10 capability deferred from 10.0.0.

## Out of Scope

- Supporting EF Core 11 previews or any EF Core major version other than 10.
- Replacing `MySqlConnector` with another database driver.
- Redesigning the public provider API or renaming established Pomelo configuration concepts without an EF Core 10 compatibility requirement.
- Implementing every new EF Core 10 feature family in the initial Pomelo 10.0.0 contribution.
- Emulating database features that MySQL or MariaDB fundamentally does not support, such as MariaDB `LATERAL`, unless an existing Pomelo contract already provides an alternative.
- Eliminating every historical skipped test unrelated to the EF Core 10 upgrade.
- Changing the supported MySQL/MariaDB version policy as part of the provider port.
- Opening a duplicate full-upgrade PR while PR #2043 remains the accepted baseline.
- Creating release tags, publishing GitHub Releases, or uploading packages to NuGet, MyGet, or another feed.
- Choosing the final preview, RC, or RTM version label on behalf of Pomelo maintainers.
- Production deployment or downstream application migration beyond the two controlled package-consumer validation applications.

## Further Notes

- Native EF Core 10 support is distinct from running Pomelo 9 and EF Core 9 on .NET 10. The latter is already runtime-compatible but does not expose the EF Core 10 provider contract.
- The selected baseline reports broad green functional coverage, but skipped tests and server-specific baselines remain part of the acceptance audit rather than automatic evidence of completeness.
- The implementation environment has a stable .NET 10 SDK and Docker available locally. PowerShell-dependent integration scripts and the Windows/database matrix are expected to be completed through fork CI after local gates pass.
- The four accepted architectural decisions cover the PR #2043 baseline and contribution strategy, support for the EF Core 10 patch line, release-grade validation without release authority, and compatibility-first scope.
- This specification is ready for implementation only when published with the `ready-for-agent` triage label.
