using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementApi.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleTypeAndApiLogsSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Modules",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "WebApp"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {   
            // Drop the Type column
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Modules"
            );
        }
    }
}
