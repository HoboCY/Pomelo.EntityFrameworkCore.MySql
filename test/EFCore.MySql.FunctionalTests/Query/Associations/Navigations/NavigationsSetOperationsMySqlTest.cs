// Copyright (c) Pomelo Foundation. All rights reserved.
// Licensed under the MIT. See LICENSE in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Pomelo.EntityFrameworkCore.MySql.FunctionalTests.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Tests.TestUtilities.Attributes;


namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsSetOperationsMySqlTest(
    NavigationsMySqlFixture fixture,
    ITestOutputHelper testOutputHelper)
    : NavigationsSetOperationsRelationalTestBase<NavigationsMySqlFixture>(fixture, testOutputHelper)
{
    [SupportedServerVersionCondition(nameof(ServerVersionSupport.CrossApply))] // MariaDB has no LATERAL support, required for correlated derived tables over collections.
    public override async Task Over_associate_collections()
    {
        await base.Over_associate_collections();

        AssertSql();
    }

    public override async Task Over_associate_collection_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        await base.Over_associate_collection_projected(queryTrackingBehavior);

        AssertSql();
    }

    [SupportedServerVersionCondition(nameof(ServerVersionSupport.CrossApply))] // MariaDB has no LATERAL support, required for correlated derived tables over collections.
    public override async Task Over_assocate_collection_Select_nested_with_aggregates_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        await base.Over_assocate_collection_Select_nested_with_aggregates_projected(queryTrackingBehavior);

        AssertSql();
    }

    [SupportedServerVersionCondition(nameof(ServerVersionSupport.CrossApply))] // MariaDB has no LATERAL support, required for correlated derived tables over collections.
    public override async Task Over_nested_associate_collection()
    {
        await base.Over_nested_associate_collection();

        AssertSql();
    }

    [SupportedServerVersionCondition(nameof(ServerVersionSupport.CrossApply))] // MariaDB has no LATERAL support, required for correlated derived tables over collections.
    public override async Task Over_different_collection_properties()
    {
        await base.Over_different_collection_properties();

        AssertSql();
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());
}
