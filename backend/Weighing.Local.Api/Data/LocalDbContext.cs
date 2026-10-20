namespace Weighing.Local.Api.Data;

public class LocalDbContext(DbContextOptions<LocalDbContext> options) : DbContext(options)
{
    public DbSet<LocalWeighingRecord> WeighingRecords => Set<LocalWeighingRecord>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<LocalWeighingRecord>(e =>
        {
            e.ToTable("WeighingRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LicensePlate).HasMaxLength(20).IsRequired();
            e.Property(x => x.FirstWeightKg).HasPrecision(18, 3);
            e.Property(x => x.SecondWeightKg).HasPrecision(18, 3);
            e.Property(x => x.CargoWeightKg).HasPrecision(18, 3);
            e.Property(x => x.ClientId).HasMaxLength(50).IsRequired();
            e.Property(x => x.LastSyncError).HasMaxLength(500);
            e.HasIndex(x => x.SyncStatus);
            e.HasIndex(x => x.WeighedAt);
            e.HasIndex(x => x.LicensePlate);
        });
        m.Entity<SyncState>(e =>
        {
            e.ToTable("SyncState");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ClientId).HasMaxLength(50).IsRequired();
        });
    }
}
