using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MijiaProductGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFavoriteNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Favorites",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Favorites");
        }
    }
}
