using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MijiaProductGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductRandomKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RandomKey",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_RandomKey",
                table: "Products",
                column: "RandomKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_RandomKey",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RandomKey",
                table: "Products");
        }
    }
}
