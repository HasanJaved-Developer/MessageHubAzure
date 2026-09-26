using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementApi.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleTypeDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Backfill NULL values
            migrationBuilder.Sql(@"
                UPDATE Modules
                SET Type = 'WebApp'
                WHERE Type IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Modules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldDefaultValue: "WebApp");      
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Modules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                defaultValue: "WebApp",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);           
        }
    }
}
