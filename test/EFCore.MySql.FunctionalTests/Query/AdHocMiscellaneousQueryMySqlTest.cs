using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using NameSpace1;
using Microting.EntityFrameworkCore.MySql.FunctionalTests.TestUtilities;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using Microting.EntityFrameworkCore.MySql.Tests.TestUtilities.Attributes;
using Xunit;

namespace Microting.EntityFrameworkCore.MySql.FunctionalTests.Query;

public class AdHocMiscellaneousQueryMySqlTest : AdHocMiscellaneousQueryRelationalTestBase
{
    public AdHocMiscellaneousQueryMySqlTest(NonSharedFixture fixture)
        : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => MySqlTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(DbContextOptionsBuilder optionsBuilder, ParameterTranslationMode parameterizedCollectionMode)
    {
        new MySqlDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    protected override Task Seed2951(Context2951 context)
        => context.Database.ExecuteSqlRawAsync(
            """
CREATE TABLE `ZeroKey` (`Id` int);
INSERT INTO `ZeroKey` VALUES (NULL)
""");

    public override async Task Multiple_different_entity_type_from_different_namespaces(bool async)
    {
        // The only change is the FromSqlRaw SQL string:
        //     Original: SELECT cast(null as int) AS MyValue
        //     Changed:  SELECT cast(null as signed) AS MyValue
        // The other comments are part of the base implementation.

        var contextFactory = await InitializeAsync<Context23981>();
        using var context = contextFactory.CreateContext();
        //var good1 = context.Set<NameSpace1.TestQuery>().FromSqlRaw(@"SELECT 1 AS MyValue").ToList(); // OK
        //var good2 = context.Set<NameSpace2.TestQuery>().FromSqlRaw(@"SELECT 1 AS MyValue").ToList(); // OK
        var bad = context.Set<TestQuery>().FromSqlRaw(@"SELECT cast(null as signed) AS MyValue").ToList(); // Exception
    }

    [ConditionalTheory]
    [SupportedServerVersionCondition(nameof(ServerVersionSupport.JsonTable))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parameterized_collection_uses_current_values(bool emptyFirst)
    {
        var contextFactory = await InitializeAsync<CollectionParameterContext>(
            seed: c => c.SeedAsync(),
            onConfiguring: b => new MySqlDbContextOptionsBuilder(b)
                .UseParameterizedCollectionMode(ParameterTranslationMode.Parameter),
            usePooling: false);
        using var context = contextFactory.CreateContext();

        var ids = emptyFirst ? new int[0] : new[] { 2, 3 };

        IQueryable<int> Query()
            => context.Entities.Where(e => ids.Contains(e.Id)).OrderBy(e => e.Id).Select(e => e.Id);

        Assert.Equal(ids, await Query().ToListAsync());

        ids = new[] { 5, 6 };
        Assert.Equal(ids, await Query().ToListAsync());
        Assert.Contains("JSON_TABLE", TestSqlLoggerFactory.SqlStatements.Last());

        ids = new int[0];
        Assert.Empty(await Query().ToListAsync());

        ids = new[] { 2 };
        Assert.Equal(ids, await Query().ToListAsync());
    }

    private class CollectionParameterEntity
    {
        public int Id { get; set; }
    }

    private class CollectionParameterContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<CollectionParameterEntity> Entities { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            new MySqlDbContextOptionsBuilder(optionsBuilder).EnablePrimitiveCollectionsSupport();
        }

        public Task SeedAsync()
        {
            AddRange(Enumerable.Range(1, 6).Select(id => new CollectionParameterEntity { Id = id }));
            return SaveChangesAsync();
        }
    }
}
