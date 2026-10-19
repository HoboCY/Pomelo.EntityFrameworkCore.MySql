# Apply inferred type mappings to parameter-backed JSON_TABLE collections

Target: `microting/Pomelo.EntityFrameworkCore.MySql:master`.
Head: `HoboCY:codex/microting-parameterized-collections` at `bc823206`.
Submission state: prepared; create as a draft after GitHub authentication is available.

Captured primitive collections used with `EnablePrimitiveCollectionsSupport()` and `UseParameterizedCollectionMode(ParameterTranslationMode.Parameter)` currently fail during query translation with `Expression '@ids' in the SQL tree does not have a type mapping assigned`.

Apply the inferred element mapping to the collection parameter and the `JSON_TABLE` value column through a provider type-mapping postprocessor. The regression test executes the same public `DbContext` query with changed values, an empty collection, and a nonempty collection again; it covers both empty and nonempty first execution.

This is a focused port of the mapping fix from [HoboCY's EF Core 10 contribution](https://github.com/HoboCY/Pomelo.EntityFrameworkCore.MySql/commit/5c3870771957505fcd6915b4ee539911976d758f), adapted to the current Microting code and namespace.

Validation on .NET SDK 10.0.100 / EF Core 10.0.13:

- Before the provider fix: both new MySQL regression cases fail with the missing-type-mapping error; neither is skipped.
- After the fix: `AdHocMiscellaneousQueryMySqlTest` passes on MySQL 8.4.3 and MariaDB 11.6.2, each with 73 passed, 0 failed, and 1 existing skip (`SaveChangesAsync_accepts_changes_with_ConfigureAwait_true`, EF issue #34727).
- Debug and Release builds of the functional test project, including the provider and its extensions: 0 warnings, 0 errors.
- `git diff --check` passes. The complete Windows/Linux database matrix was not run locally.

Draft pending maintainer review and the full CI matrix.
