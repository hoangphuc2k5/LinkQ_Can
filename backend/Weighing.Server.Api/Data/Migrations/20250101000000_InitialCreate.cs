using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Weighing.Server.Api.Data.Migrations;

[DbContext(typeof(ServerDbContext))]
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
                CreatedAtServer = t.Column<DateTime>(nullable: false),
                ServerSequence = t.Column<long>(nullable: false)
            },
            constraints: t => t.PrimaryKey("PK_WeighingRecords", x => x.Id));
        mb.CreateIndex("IX_WeighingRecords_ServerSequence", "WeighingRecords", "ServerSequence", unique: true);
        mb.CreateIndex("IX_WeighingRecords_WeighedAt", "WeighingRecords", "WeighedAt");
        mb.CreateIndex("IX_WeighingRecords_ClientId", "WeighingRecords", "ClientId");

        mb.CreateTable(
            name: "SyncCounters",
            columns: t => new { Id = t.Column<int>(nullable: false), Value = t.Column<long>(nullable: false) },
            constraints: t => t.PrimaryKey("PK_SyncCounters", x => x.Id));
        // Seed dòng bộ đếm bằng SQL thuần (chạy được trên cả SQL Server và SQLite test, không phụ thuộc model EF).
        mb.Sql("INSERT INTO SyncCounters (Id, Value) VALUES (1, 0);");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("WeighingRecords");
        mb.DropTable("SyncCounters");
    }
}
