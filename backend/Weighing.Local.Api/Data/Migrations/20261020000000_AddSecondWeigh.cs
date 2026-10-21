using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Weighing.Local.Api.Data.Migrations;

[DbContext(typeof(LocalDbContext))]
[Migration("20261020000000_AddSecondWeigh")]
public class AddSecondWeigh : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.RenameColumn(name: "WeightKg", table: "WeighingRecords", newName: "FirstWeightKg");
        mb.AddColumn<decimal>(name: "SecondWeightKg", table: "WeighingRecords", precision: 18, scale: 3, nullable: true);
        mb.AddColumn<decimal>(name: "CargoWeightKg", table: "WeighingRecords", precision: 18, scale: 3, nullable: true);
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropColumn(name: "CargoWeightKg", table: "WeighingRecords");
        mb.DropColumn(name: "SecondWeightKg", table: "WeighingRecords");
        mb.RenameColumn(name: "FirstWeightKg", table: "WeighingRecords", newName: "WeightKg");
    }
}
