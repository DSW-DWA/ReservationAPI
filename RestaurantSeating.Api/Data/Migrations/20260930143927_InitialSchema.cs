using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSeating.Api.Data.Migrations;

public partial class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RestaurantTables",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Capacity = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RestaurantTables", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GuestGroups",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Size = table.Column<int>(type: "int", nullable: false),
                ArrivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Status = table.Column<string>(type: "varchar(16)", nullable: false),
                TableId = table.Column<int>(type: "int", nullable: true),
                SeatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GuestGroups", x => x.Id);
                table.ForeignKey(
                    name: "FK_GuestGroups_RestaurantTables_TableId",
                    column: x => x.TableId,
                    principalTable: "RestaurantTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_GuestGroups_Status_Id",
            table: "GuestGroups",
            columns: new[] { "Status", "Id" })
            .Annotation("SqlServer:Include", new[] { "Size", "TableId", "ArrivedAt", "SeatedAt", "EndedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_GuestGroups_TableId",
            table: "GuestGroups",
            column: "TableId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "GuestGroups");

        migrationBuilder.DropTable(
            name: "RestaurantTables");
    }
}
