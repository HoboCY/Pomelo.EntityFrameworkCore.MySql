// Copyright (c) Pomelo Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Pomelo.EntityFrameworkCore.MySql.FunctionalTests.TestUtilities;
using Xunit;

namespace Pomelo.EntityFrameworkCore.MySql.FunctionalTests;

public class OwnedJsonRoundTripMySqlTest
    : OwnedJsonRoundTripTestBase<OwnedJsonRoundTripMySqlTest.MySqlFixture>
{
    public OwnedJsonRoundTripMySqlTest(MySqlFixture fixture)
        : base(fixture)
    {
    }

    public sealed class MySqlFixture : OwnedJsonRoundTripFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => MySqlTestStoreFactory.Instance;
    }
}

public class OwnedJsonRoundTripMySqlNoBackslashEscapesTest
    : OwnedJsonRoundTripTestBase<OwnedJsonRoundTripMySqlNoBackslashEscapesTest.MySqlFixture>
{
    public OwnedJsonRoundTripMySqlNoBackslashEscapesTest(MySqlFixture fixture)
        : base(fixture)
    {
    }

    public sealed class MySqlFixture : OwnedJsonRoundTripFixtureBase
    {
        protected override string StoreName
            => "OwnedJsonRoundTripNBE";

        protected override ITestStoreFactory TestStoreFactory
            => MySqlTestStoreFactory.NoBackslashEscapesInstance;
    }
}

public abstract class OwnedJsonRoundTripTestBase<TFixture>(TFixture fixture)
    : IClassFixture<TFixture>
    where TFixture : OwnedJsonRoundTripFixtureBase, new()
{
    private const string InitialDescription = "O'Reilly \\ archive — 初始 😀";
    private const string UpdatedDescription = "\"quoted\" \\ updated — 更新 🧪";

    private const int DescriptionDocumentId = 1;
    private const int NestedDocumentId = 2;
    private const int CollectionDocumentId = 3;
    private const int SpecialNameDocumentId = 4;
    private const int BooleanDocumentId = 5;
    private const int NullDocumentId = 6;
    private const int UnequalBooleanDocumentId = 7;
    private const int MixedBooleanDocumentId = 8;
    private const int UnequalMixedBooleanDocumentId = 9;
    private const int EqualBooleanDocumentId = 10;
    private const int AmbiguousNameDocumentId = 11;
    private const int EscapedNameDocumentId = 12;
    private const int StructuralDocumentId = 13;
    private const int RootCollectionDocumentId = 14;

    protected TFixture Fixture { get; } = fixture;

    [Fact]
    public async Task Owned_json_value_round_trips_and_updates()
    {
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(
                candidate => candidate.Settings.Description == InitialDescription);

            Assert.Equal(InitialDescription, document.Settings!.Description);
            document.Settings.Description = UpdatedDescription;

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var updatedDocument = await context.Documents.SingleAsync(
                candidate => candidate.Settings.Description == UpdatedDescription);

            Assert.Equal(UpdatedDescription, updatedDocument.Settings!.Description);
            updatedDocument.Settings.Description = string.Empty;

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var emptyDocument = await verificationContext.Documents.SingleAsync(
            candidate => candidate.Settings.Description == string.Empty);

        Assert.Equal(string.Empty, emptyDocument.Settings!.Description);
    }

    [Fact]
    public async Task Owned_json_nested_members_are_persisted_and_reload_with_a_new_context()
    {
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == NestedDocumentId);

            // Modifying more than one member of the nested owned entity rewrites the nested JSON document.
            document.Settings.Nested.First = "nested-first-updated";
            document.Settings.Nested.Second = "nested-second-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.Documents.SingleAsync(candidate => candidate.Id == NestedDocumentId);

        Assert.Equal("nested-first-updated", updatedDocument.Settings.Nested.First);
        Assert.Equal("nested-second-updated", updatedDocument.Settings.Nested.Second);
        Assert.Equal("nested-description", updatedDocument.Settings.Description);
    }

    [Fact]
    public async Task Owned_json_collection_elements_are_persisted_and_reload_with_a_new_context()
    {
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == CollectionDocumentId);

            // Modifying more than one member of an element rewrites the element JSON document, addressed by its position.
            document.Settings.Items[1].First = "item-first-updated";
            document.Settings.Items[1].Second = "item-second-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == CollectionDocumentId);
            document.Settings.Items[0].Second = "item0-second-updated";

            await context.SaveChangesAsync();
        }

        // Members of two different elements change in one SaveChanges. EF Core 10 then truncates the JSON
        // path at the collection itself, so the whole array is written through the collection fragment path:
        // the rewrite has to keep the array shape and must not lose the members that are not part of the update.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == CollectionDocumentId);
            document.Settings.Items[0].First = "item0-first-updated";
            document.Settings.Items[1].Second = "item1-second-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.Documents.SingleAsync(candidate => candidate.Id == CollectionDocumentId);

        Assert.Equal(2, updatedDocument.Settings.Items.Count);
        Assert.Equal("item0-first-updated", updatedDocument.Settings.Items[0].First);
        Assert.Equal("item0-second-updated", updatedDocument.Settings.Items[0].Second);
        Assert.Equal("item-first-updated", updatedDocument.Settings.Items[1].First);
        Assert.Equal("item1-second-updated", updatedDocument.Settings.Items[1].Second);

        // The members of the parent document and of the nested owned entity are untouched by the rewrite.
        Assert.Equal("collection-description", updatedDocument.Settings.Description);
        Assert.Equal("collection-nested-first", updatedDocument.Settings.Nested.First);
        Assert.Equal("collection-nested-second", updatedDocument.Settings.Nested.Second);

        using var storedDocument = JsonDocument.Parse(
            await ReadStoredSettingsAsync(verificationContext, CollectionDocumentId));

        Assert.Equal(
            JsonValueKind.Array,
            storedDocument.RootElement.GetProperty("items.array").ValueKind);
    }

    [Fact]
    public async Task Owned_json_null_and_root_members_keep_json_semantics()
    {
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == NullDocumentId);
            document.Settings.Nested = null;

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            using var storedDocument = JsonDocument.Parse(
                await ReadStoredSettingsAsync(context, NullDocumentId));

            Assert.Equal(
                JsonValueKind.Null,
                storedDocument.RootElement.GetProperty("nested.document").ValueKind);
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == NullDocumentId);

            // Replacing the whole owned reference rewrites the complete JSON document.
            document.Settings = new OwnedJsonRoundTripSettings
            {
                Description = "replaced-description",
                Enabled = true,
                OptionalFlag = false,
                DottedName = "replaced-dot",
                QuotedName = "replaced-quote",
                BackslashName = "replaced-backslash",
                NumberConvertedFlag = true,
                TextConvertedFlag = false,
                Nested = new OwnedJsonRoundTripNested { First = "replaced-first", Second = "replaced-second" },
                Items = new List<OwnedJsonRoundTripNested> { new() { First = "replaced-item", Second = "replaced-item-second" } },
            };

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var replacedDocument = await verificationContext.Documents.SingleAsync(candidate => candidate.Id == NullDocumentId);

        Assert.Equal("replaced-description", replacedDocument.Settings.Description);
        Assert.Equal("replaced-first", replacedDocument.Settings.Nested.First);
        Assert.Equal("replaced-second", replacedDocument.Settings.Nested.Second);
        Assert.Equal("replaced-dot", replacedDocument.Settings.DottedName);
        Assert.Equal("replaced-quote", replacedDocument.Settings.QuotedName);
        Assert.Equal("replaced-backslash", replacedDocument.Settings.BackslashName);
        Assert.Single(replacedDocument.Settings.Items);
        Assert.Equal("replaced-item", replacedDocument.Settings.Items[0].First);
    }

    [Fact]
    public async Task Owned_json_boolean_members_filter_project_and_preserve_null_values()
    {
        await using (var context = Fixture.CreateContext())
        {
            Assert.True(await context.Documents
                .Where(candidate => candidate.Id == BooleanDocumentId)
                .Select(candidate => candidate.Settings.Enabled)
                .SingleAsync());

            Assert.Null(await context.Documents
                .Where(candidate => candidate.Id == BooleanDocumentId)
                .Select(candidate => candidate.Settings.OptionalFlag)
                .SingleAsync());

            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.Enabled));
            Assert.Equal(0, await context.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && !candidate.Settings.Enabled));
            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.OptionalFlag == null));

            // Boolean members that use a value converter keep their configured converter.
            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.NumberConvertedFlag));
            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.TextConvertedFlag));
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == BooleanDocumentId);
            document.Settings.Enabled = false;
            document.Settings.OptionalFlag = true;
            document.Settings.NumberConvertedFlag = false;
            document.Settings.TextConvertedFlag = false;

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();

        Assert.False(await verificationContext.Documents
            .Where(candidate => candidate.Id == BooleanDocumentId)
            .Select(candidate => candidate.Settings.Enabled)
            .SingleAsync());

        Assert.True(await verificationContext.Documents
            .Where(candidate => candidate.Id == BooleanDocumentId)
            .Select(candidate => candidate.Settings.OptionalFlag)
            .SingleAsync());

        Assert.Equal(0, await verificationContext.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.Enabled));
        Assert.Equal(1, await verificationContext.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && !candidate.Settings.Enabled));
        Assert.Equal(1, await verificationContext.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.OptionalFlag == true));
        Assert.Equal(0, await verificationContext.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.NumberConvertedFlag));
        Assert.Equal(0, await verificationContext.Documents.CountAsync(candidate => candidate.Id == BooleanDocumentId && candidate.Settings.TextConvertedFlag));
    }

    [Fact]
    public async Task Owned_json_boolean_members_can_be_compared_to_each_other()
    {
        await using var context = Fixture.CreateContext();

        // Comparing two JSON boolean members keeps each comparison atomic and returns the documents whose
        // members are equal.
        var equalDocuments = await context.Documents
            .Where(
                candidate => candidate.Id >= UnequalBooleanDocumentId &&
                             candidate.Id <= EqualBooleanDocumentId &&
                             candidate.Settings.Enabled == candidate.Settings.Active)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .ToListAsync();

        Assert.Equal(new[] { UnequalBooleanDocumentId, EqualBooleanDocumentId }, equalDocuments);

        // The same comparison is also correct when it is projected or combined with another condition.
        var projected = await context.Documents
            .Where(candidate => candidate.Id >= UnequalBooleanDocumentId && candidate.Id <= EqualBooleanDocumentId)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, Same = candidate.Settings.Enabled == candidate.Settings.Active })
            .ToListAsync();

        Assert.Equal(new[] { true, false, false, true }, projected.Select(item => item.Same));

        var combined = await context.Documents
            .Where(
                candidate => candidate.Id >= UnequalBooleanDocumentId &&
                             candidate.Id <= EqualBooleanDocumentId &&
                             candidate.Settings.Enabled &&
                             candidate.Settings.Enabled == candidate.Settings.Active)
            .Select(candidate => candidate.Id)
            .ToListAsync();

        Assert.Equal(new[] { EqualBooleanDocumentId }, combined);
    }

    [Fact]
    public async Task Owned_json_members_with_names_that_are_not_plain_path_segments_are_queryable_and_updatable()
    {
        // A JSON name that starts with a digit cannot be used as an unquoted path segment, and a name that
        // contains a control character cannot be used unescaped.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == EscapedNameDocumentId);
            document.Settings.NumberedName = "numbered-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == EscapedNameDocumentId);
            Assert.Equal("numbered-updated", document.Settings.NumberedName);

            document.Settings.ControlName = "control-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.Documents.SingleAsync(candidate => candidate.Id == EscapedNameDocumentId);

        Assert.Equal("numbered-updated", updatedDocument.Settings.NumberedName);
        Assert.Equal("control-updated", updatedDocument.Settings.ControlName);
        Assert.Equal(1, await verificationContext.Documents.CountAsync(candidate => candidate.Settings.NumberedName == "numbered-updated"));
        Assert.Equal(1, await verificationContext.Documents.CountAsync(candidate => candidate.Settings.ControlName == "control-updated"));
    }

    [Fact]
    public async Task Owned_json_member_paths_stay_unambiguous_when_a_property_name_matches_a_nested_path()
    {
        // A scalar member whose JSON name is the complete path of a nested owned member must not be
        // confused with that nested member.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == AmbiguousNameDocumentId);
            document.Settings.Nested.First = "nested-first-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == AmbiguousNameDocumentId);

            Assert.Equal("nested-first-updated", document.Settings.Nested.First);
            Assert.Equal("flattened-first-initial", document.Settings.FlattenedFirst);
        }

        // The same path, modified through the scalar member, must only update that member.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == AmbiguousNameDocumentId);
            document.Settings.FlattenedFirst = "flattened-first-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.Documents.SingleAsync(candidate => candidate.Id == AmbiguousNameDocumentId);

        Assert.Equal("flattened-first-updated", updatedDocument.Settings.FlattenedFirst);
        Assert.Equal("nested-first-updated", updatedDocument.Settings.Nested.First);
        Assert.Equal("nested-second-initial", updatedDocument.Settings.Nested.Second);
    }

    [Fact]
    public async Task Owned_json_boolean_members_use_the_compatibility_query_path()
    {
        await using var context = Fixture.CreateContext();
        var connectionString = context.Database.GetConnectionString();

        // MySQL 5.7 has no JSON_VALUE(), so the provider falls back to JSON_UNQUOTE(JSON_EXTRACT(...)) for
        // JSON scalars. That path has to convert JSON booleans as well and keep JSON null values NULL.
        var compatibilityOptions = new DbContextOptionsBuilder<OwnedJsonRoundTripContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(5, 7, 44)))
            .UseModel(context.Model)
            .Options;

        await using var compatibilityContext = new OwnedJsonRoundTripContext(compatibilityOptions);

        Assert.Equal(
            2,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id >= 7 && candidate.Id <= 10 && candidate.Settings.Enabled));

        Assert.Equal(
            2,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id >= 7 && candidate.Id <= 10 && !candidate.Settings.Enabled));

        Assert.Equal(
            1,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id == 8 && candidate.Settings.Enabled));

        Assert.Equal(
            0,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id == 7 && candidate.Settings.Enabled));

        // JSON null and JSON false are distinct values and must not collapse into each other.
        Assert.Equal(
            1,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id == 8 && candidate.Settings.OptionalFlag == null));

        Assert.Equal(
            1,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id == 9 && candidate.Settings.OptionalFlag == false));

        Assert.Equal(
            0,
            await compatibilityContext.Documents.CountAsync(
                candidate => candidate.Id == 9 && candidate.Settings.OptionalFlag == true));

        // The same fallback has to produce correct projection values: JSON false and JSON true keep their
        // value, and JSON null stays null instead of collapsing into false or into a non-null value.
        var projectedMembers = await compatibilityContext.Documents
            .Where(candidate => candidate.Id >= UnequalBooleanDocumentId && candidate.Id <= EqualBooleanDocumentId)
            .OrderBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.Settings.Enabled, candidate.Settings.OptionalFlag })
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                UnequalBooleanDocumentId,
                MixedBooleanDocumentId,
                UnequalMixedBooleanDocumentId,
                EqualBooleanDocumentId,
            },
            projectedMembers.Select(item => item.Id));

        Assert.Equal(
            new[] { false, true, false, true },
            projectedMembers.Select(item => item.Enabled));

        Assert.Equal(
            new bool?[] { null, null, false, null },
            projectedMembers.Select(item => item.OptionalFlag));
    }

    [Fact]
    public async Task Owned_json_members_with_special_names_are_queryable_and_updatable()
    {
        // A single modified member of a nested owned entity with a dot in its JSON name.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == SpecialNameDocumentId);
            document.Settings.Nested.First = "nested-first-updated";

            await context.SaveChangesAsync();
        }

        // A member whose JSON name contains a dot.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == SpecialNameDocumentId);
            document.Settings.DottedName = "dotted-updated";

            await context.SaveChangesAsync();
        }

        // A member whose JSON name contains a double quote.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == SpecialNameDocumentId);
            document.Settings.QuotedName = "quoted-updated";

            await context.SaveChangesAsync();
        }

        // A member whose JSON name contains a backslash.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == SpecialNameDocumentId);
            document.Settings.BackslashName = "backslash-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.Documents.SingleAsync(candidate => candidate.Id == SpecialNameDocumentId);

            Assert.Equal("nested-first-updated", document.Settings.Nested.First);

            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Settings.DottedName == "dotted-updated"));
            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Settings.QuotedName == "quoted-updated"));
            Assert.Equal(1, await context.Documents.CountAsync(candidate => candidate.Settings.BackslashName == "backslash-updated"));
        }
    }

    [Fact]
    public async Task Owned_json_structural_navigation_updates_stay_unambiguous_in_both_directions()
    {
        // The model contains two members that produce the same JSON path text: the nested Inner.Leaf member
        // and the Dotted member whose JSON name is the complete path "Inner.Leaf". Updating both members of
        // one of them must only update that member, and must leave the other member untouched, in both
        // directions.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.StructuralDocuments.SingleAsync(candidate => candidate.Id == StructuralDocumentId);

            document.Data.Inner.Leaf.X = "inner-leaf-x-updated";
            document.Data.Inner.Leaf.Y = "inner-leaf-y-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.StructuralDocuments.SingleAsync(candidate => candidate.Id == StructuralDocumentId);

            Assert.Equal("inner-leaf-x-updated", document.Data.Inner.Leaf.X);
            Assert.Equal("inner-leaf-y-updated", document.Data.Inner.Leaf.Y);
            Assert.Equal("dotted-x", document.Data.Dotted.X);
            Assert.Equal("dotted-y", document.Data.Dotted.Y);
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.StructuralDocuments.SingleAsync(candidate => candidate.Id == StructuralDocumentId);

            document.Data.Dotted.X = "dotted-x-updated";
            document.Data.Dotted.Y = "dotted-y-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.StructuralDocuments.SingleAsync(candidate => candidate.Id == StructuralDocumentId);

        Assert.Equal("dotted-x-updated", updatedDocument.Data.Dotted.X);
        Assert.Equal("dotted-y-updated", updatedDocument.Data.Dotted.Y);
        Assert.Equal("inner-leaf-x-updated", updatedDocument.Data.Inner.Leaf.X);
        Assert.Equal("inner-leaf-y-updated", updatedDocument.Data.Inner.Leaf.Y);
    }

    [Fact]
    public async Task Owned_json_root_collection_updates_keep_the_element_position()
    {
        // A root owned collection is stored as a JSON array, so the position of an updated element is part
        // of its JSON path. The member JSON name contains a dot, which must not be used as a path separator.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.CollectionDocuments.SingleAsync(candidate => candidate.Id == RootCollectionDocumentId);

            document.Items[1].X = "item1-x-updated";

            await context.SaveChangesAsync();
        }

        await using (var context = Fixture.CreateContext())
        {
            var document = await context.CollectionDocuments.SingleAsync(candidate => candidate.Id == RootCollectionDocumentId);

            Assert.Equal("item0-x", document.Items[0].X);
            Assert.Equal("item0-y", document.Items[0].Y);
            Assert.Equal("item1-x-updated", document.Items[1].X);
            Assert.Equal("item1-y", document.Items[1].Y);
        }

        // Rewriting a complete element keeps its position as well.
        await using (var context = Fixture.CreateContext())
        {
            var document = await context.CollectionDocuments.SingleAsync(candidate => candidate.Id == RootCollectionDocumentId);

            document.Items[0].X = "item0-x-updated";
            document.Items[0].Y = "item0-y-updated";

            await context.SaveChangesAsync();
        }

        await using var verificationContext = Fixture.CreateContext();
        var updatedDocument = await verificationContext.CollectionDocuments.SingleAsync(candidate => candidate.Id == RootCollectionDocumentId);

        Assert.Equal("item0-x-updated", updatedDocument.Items[0].X);
        Assert.Equal("item0-y-updated", updatedDocument.Items[0].Y);
        Assert.Equal("item1-x-updated", updatedDocument.Items[1].X);
        Assert.Equal("item1-y", updatedDocument.Items[1].Y);
    }

    private static async Task<string> ReadStoredSettingsAsync(OwnedJsonRoundTripContext context, int id)
    {
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT `Settings` FROM `Documents` WHERE `Id` = {id}";

        return (string)(await command.ExecuteScalarAsync())!;
    }
}

public abstract class OwnedJsonRoundTripFixtureBase
    : SharedStoreFixtureBase<OwnedJsonRoundTripContext>
{
    protected override string StoreName
        => "OwnedJsonRoundTripMySqlTest";

    protected override Task SeedAsync(OwnedJsonRoundTripContext context)
    {
        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 1,
                Settings = new OwnedJsonRoundTripSettings { Description = "O'Reilly \\ archive — 初始 😀" }
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 2,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "nested-description",
                    Nested = new OwnedJsonRoundTripNested { First = "nested-first", Second = "nested-second" },
                },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 3,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "collection-description",
                    Nested = new OwnedJsonRoundTripNested { First = "collection-nested-first", Second = "collection-nested-second" },
                    Items = new List<OwnedJsonRoundTripNested>
                    {
                        new() { First = "item0-first", Second = "item0-second" },
                        new() { First = "item-first", Second = "item-second" },
                    },
                },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 4,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "special-name-description",
                    DottedName = "dotted-initial",
                    QuotedName = "quoted-initial",
                    BackslashName = "backslash-initial",
                    Nested = new OwnedJsonRoundTripNested { First = "special-nested-first", Second = "special-nested-second" },
                },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 5,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "boolean-description",
                    Enabled = true,
                    OptionalFlag = null,
                    NumberConvertedFlag = true,
                    TextConvertedFlag = true,
                },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 6,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "null-description",
                    Nested = new OwnedJsonRoundTripNested { First = "null-nested-first", Second = "null-nested-second" },
                },
            });

        context.Documents.AddRange(
            new OwnedJsonRoundTripDocument
            {
                Id = 7,
                Settings = new OwnedJsonRoundTripSettings { Description = "unequal-boolean-description", Enabled = false, Active = false },
            },
            new OwnedJsonRoundTripDocument
            {
                Id = 8,
                Settings = new OwnedJsonRoundTripSettings { Description = "mixed-boolean-description", Enabled = true, Active = false },
            },
            new OwnedJsonRoundTripDocument
            {
                Id = 9,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "unequal-mixed-boolean-description",
                    Enabled = false,
                    Active = true,
                    OptionalFlag = false,
                },
            },
            new OwnedJsonRoundTripDocument
            {
                Id = 10,
                Settings = new OwnedJsonRoundTripSettings { Description = "equal-boolean-description", Enabled = true, Active = true },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 11,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "ambiguous-name-description",
                    FlattenedFirst = "flattened-first-initial",
                    Nested = new OwnedJsonRoundTripNested { First = "nested-first-initial", Second = "nested-second-initial" },
                },
            });

        context.Documents.Add(
            new OwnedJsonRoundTripDocument
            {
                Id = 12,
                Settings = new OwnedJsonRoundTripSettings
                {
                    Description = "escaped-name-description",
                    NumberedName = "numbered-initial",
                    ControlName = "control-initial",
                },
            });

        context.StructuralDocuments.Add(
            new OwnedJsonStructuralDocument
            {
                Id = 13,
                Data = new OwnedJsonStructuralData
                {
                    Inner = new OwnedJsonStructuralInner
                    {
                        Leaf = new OwnedJsonStructuralNode { X = "inner-leaf-x", Y = "inner-leaf-y" },
                    },
                    Dotted = new OwnedJsonStructuralNode { X = "dotted-x", Y = "dotted-y" },
                },
            });

        context.CollectionDocuments.Add(
            new OwnedJsonCollectionDocument
            {
                Id = 14,
                Items = new List<OwnedJsonCollectionItem>
                {
                    new() { X = "item0-x", Y = "item0-y" },
                    new() { X = "item1-x", Y = "item1-y" },
                },
            });

        return context.SaveChangesAsync();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        modelBuilder.Entity<OwnedJsonRoundTripDocument>(builder =>
        {
            builder.Property(document => document.Id).ValueGeneratedNever();
            builder.OwnsOne(
                document => document.Settings,
                ownedBuilder =>
                {
                    ownedBuilder.ToJson();
                    ownedBuilder.Property(settings => settings.DottedName).HasJsonPropertyName("display.name");
                    ownedBuilder.Property(settings => settings.QuotedName).HasJsonPropertyName("quoted\"name");
                    ownedBuilder.Property(settings => settings.BackslashName).HasJsonPropertyName("backslash\\name");
                    ownedBuilder.Property(settings => settings.FlattenedFirst).HasJsonPropertyName("nested.document.First");
                    ownedBuilder.Property(settings => settings.NumberedName).HasJsonPropertyName("1numbered");
                    ownedBuilder.Property(settings => settings.ControlName).HasJsonPropertyName("control\u0001name");
                    ownedBuilder.Property(settings => settings.NumberConvertedFlag).HasConversion<int>();
                    ownedBuilder.Property(settings => settings.TextConvertedFlag).HasConversion<string>();
                    ownedBuilder.OwnsOne(settings => settings.Nested, nestedBuilder => nestedBuilder.HasJsonPropertyName("nested.document"));
                    ownedBuilder.OwnsMany(settings => settings.Items, itemsBuilder => itemsBuilder.HasJsonPropertyName("items.array"));
                });
        });

        modelBuilder.Entity<OwnedJsonStructuralDocument>(builder =>
        {
            builder.Property(document => document.Id).ValueGeneratedNever();
            builder.OwnsOne(
                document => document.Data,
                ownedBuilder =>
                {
                    ownedBuilder.ToJson();
                    ownedBuilder.OwnsOne(data => data.Inner, innerBuilder => innerBuilder.OwnsOne(inner => inner.Leaf));
                    ownedBuilder.OwnsOne(data => data.Dotted, dottedBuilder => dottedBuilder.HasJsonPropertyName("Inner.Leaf"));
                });
        });

        modelBuilder.Entity<OwnedJsonCollectionDocument>(builder =>
        {
            builder.Property(document => document.Id).ValueGeneratedNever();
            builder.OwnsMany(
                document => document.Items,
                itemsBuilder =>
                {
                    itemsBuilder.ToJson();
                    itemsBuilder.Property(item => item.X).HasJsonPropertyName("x.y");
                });
        });
    }
}

public sealed class OwnedJsonRoundTripContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OwnedJsonRoundTripDocument> Documents
        => Set<OwnedJsonRoundTripDocument>();

    public DbSet<OwnedJsonStructuralDocument> StructuralDocuments
        => Set<OwnedJsonStructuralDocument>();

    public DbSet<OwnedJsonCollectionDocument> CollectionDocuments
        => Set<OwnedJsonCollectionDocument>();
}

public sealed class OwnedJsonRoundTripDocument
{
    public int Id { get; set; }
    public OwnedJsonRoundTripSettings Settings { get; set; } = null!;
}

public sealed class OwnedJsonRoundTripSettings
{
    public string Description { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool Active { get; set; }
    public bool? OptionalFlag { get; set; }
    public string DottedName { get; set; } = null!;
    public string QuotedName { get; set; } = null!;
    public string BackslashName { get; set; } = null!;
    public string FlattenedFirst { get; set; } = null!;
    public string NumberedName { get; set; } = null!;
    public string ControlName { get; set; } = null!;
    public bool NumberConvertedFlag { get; set; }
    public bool TextConvertedFlag { get; set; }
    public OwnedJsonRoundTripNested Nested { get; set; } = null!;
    public List<OwnedJsonRoundTripNested> Items { get; set; } = null!;
}

public sealed class OwnedJsonRoundTripNested
{
    public string First { get; set; } = null!;
    public string Second { get; set; } = null!;
}

public sealed class OwnedJsonStructuralDocument
{
    public int Id { get; set; }
    public OwnedJsonStructuralData Data { get; set; } = null!;
}

public sealed class OwnedJsonStructuralData
{
    public OwnedJsonStructuralInner Inner { get; set; } = null!;
    public OwnedJsonStructuralNode Dotted { get; set; } = null!;
}

public sealed class OwnedJsonStructuralInner
{
    public OwnedJsonStructuralNode Leaf { get; set; } = null!;
}

public sealed class OwnedJsonStructuralNode
{
    public string X { get; set; } = null!;
    public string Y { get; set; } = null!;
}

public sealed class OwnedJsonCollectionDocument
{
    public int Id { get; set; }
    public List<OwnedJsonCollectionItem> Items { get; set; } = null!;
}

public sealed class OwnedJsonCollectionItem
{
    public string X { get; set; } = null!;
    public string Y { get; set; } = null!;
}
