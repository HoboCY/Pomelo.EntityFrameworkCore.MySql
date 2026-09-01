using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("POMELO_PACKAGE_CONSUMER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("POMELO_PACKAGE_CONSUMER_CONNECTION_STRING is required.");

var serverType = Environment.GetEnvironmentVariable("POMELO_PACKAGE_CONSUMER_SERVER_TYPE")
    ?? throw new InvalidOperationException("POMELO_PACKAGE_CONSUMER_SERVER_TYPE is required.");

var serverVersion = ServerVersion.AutoDetect(connectionString);
if (!string.Equals(serverVersion.TypeIdentifier, serverType, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Expected a {serverType} server, but the connected server reported {serverVersion}.");
}

Console.WriteLine($"Package consumer: provider={typeof(MySqlDbContextOptionsBuilderExtensions).Assembly.GetName().Version}, " +
                  $"efcore={typeof(DbContext).Assembly.GetName().Version}, server={serverVersion}");

// Compile and construct each public extension package through the package boundary.
_ = new DbContextOptionsBuilder<SmokeContext>()
    .UseMySql(connectionString, serverVersion, options => options.UseMicrosoftJson())
    .Options;
_ = new DbContextOptionsBuilder<SmokeContext>()
    .UseMySql(connectionString, serverVersion, options => options.UseNewtonsoftJson())
    .Options;
_ = new DbContextOptionsBuilder<SmokeContext>()
    .UseMySql(connectionString, serverVersion, options => options.UseNetTopologySuite())
    .Options;

var optionsBuilder = new DbContextOptionsBuilder<SmokeContext>()
    .UseMySql(connectionString, serverVersion);

var committedItemId = 0;

await using (var context = new SmokeContext(optionsBuilder.Options))
{
    await context.Database.EnsureDeletedAsync();
    await context.Database.EnsureCreatedAsync();

    await using var transaction = await context.Database.BeginTransactionAsync();

    var item = new SmokeItem { Name = "created" };
    context.Items.Add(item);
    await context.SaveChangesAsync();

    if (item.Id <= 0)
    {
        throw new InvalidOperationException("The database did not generate an identity key.");
    }

    item.Name = "updated";
    await context.SaveChangesAsync();

    var updated = await context.Items.SingleAsync(candidate => candidate.Id == item.Id);
    if (updated.Name != "updated")
    {
        throw new InvalidOperationException("The update round trip returned an unexpected value.");
    }

    await transaction.CommitAsync();
    committedItemId = updated.Id;
}

await using (var context = new SmokeContext(optionsBuilder.Options))
{
    var committed = await context.Items.SingleOrDefaultAsync(item => item.Id == committedItemId);
    if (committed is null || committed.Name != "updated")
    {
        throw new InvalidOperationException("The committed row was not visible from a new context.");
    }

    context.Items.Remove(committed);
    await context.SaveChangesAsync();
}

await using (var context = new SmokeContext(optionsBuilder.Options))
{
    if (await context.Items.AnyAsync())
    {
        throw new InvalidOperationException("The delete round trip left rows in the database.");
    }
}

Console.WriteLine("Package consumer smoke passed: model creation, CRUD, and transaction commit.");

public sealed class SmokeContext(DbContextOptions<SmokeContext> options) : DbContext(options)
{
    public DbSet<SmokeItem> Items => Set<SmokeItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<SmokeItem>(entity =>
        {
            entity.ToTable("PackageConsumerItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(64).IsRequired();
        });
}

public sealed class SmokeItem
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;
}
