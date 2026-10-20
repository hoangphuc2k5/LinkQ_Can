namespace Weighing.Server.Api.Data;

public class ServerDbContext(DbContextOptions<ServerDbContext> options) : DbContext(options)
{
    public DbSet<WeighingRecordEntity> WeighingRecords => Set<WeighingRecordEntity>();
    public DbSet<SyncCounter> SyncCounters => Set<SyncCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<WeighingRecordEntity>(e =>
        {
            e.ToTable("WeighingRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LicensePlate).HasMaxLength(20).IsRequired();
            e.Property(x => x.FirstWeightKg).HasPrecision(18, 3);
            e.Property(x => x.SecondWeightKg).HasPrecision(18, 3);
            e.Property(x => x.CargoWeightKg).HasPrecision(18, 3);
            e.Property(x => x.ClientId).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.ServerSequence).IsUnique();
            e.HasIndex(x => x.WeighedAt);
            e.HasIndex(x => x.ClientId);
        });
        m.Entity<SyncCounter>(e =>
        {
            e.ToTable("SyncCounters");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(new SyncCounter { Id = SyncCounter.SingletonId, Value = 0 });
        });
    }
}
