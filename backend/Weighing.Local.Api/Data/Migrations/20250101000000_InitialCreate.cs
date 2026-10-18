using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Weighing.Local.Api.Data.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20250101000000_InitialCreate")]
public class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable(
            name: "WeighingRecords",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                LicensePlate = t.Column<string>(maxLength: 20, nullable: false),
                WeightKg = t.Column<decimal>(precision: 18, scale: 3, nullable: false),
                Direction = t.Column<int>(nullable: false),
                CargoStatus = t.Column<int>(nullable: false),
                WeighedAt = t.Column<DateTime>(nullable: false),
                ClientId = t.Column<string>(maxLength: 50, nullable: false),
                SyncStatus = t.Column<int>(nullable: false),
                SyncedAt = t.Column<DateTime>(nullable: true),
                LastSyncError = t.Column<string>(maxLength: 500, nullable: true),
                RetryCount = t.Column<int>(nullable: false),
                ServerSequence = t.Column<long>(nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_WeighingRecords", x => x.Id));
        mb.CreateIndex("IX_WeighingRecords_SyncStatus", "WeighingRecords", "SyncStatus");
        mb.CreateIndex("IX_WeighingRecords_WeighedAt", "WeighingRecords", "WeighedAt");
        mb.CreateIndex("IX_WeighingRecords_LicensePlate", "WeighingRecords", "LicensePlate");

        mb.CreateTable(
            name: "SyncState",
            columns: t => new
            {
                Id = t.Column<int>(nullable: false),
                ClientId = t.Column<string>(maxLength: 50, nullable: false),
                LastServerCursor = t.Column<long>(nullable: false),
                LastSuccessfulSyncAt = t.Column<DateTime>(nullable: true)
            },
            constraints: t => t.PrimaryKey("PK_SyncState", x => x.Id));
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("WeighingRecords");
        mb.DropTable("SyncState");
    }
}
