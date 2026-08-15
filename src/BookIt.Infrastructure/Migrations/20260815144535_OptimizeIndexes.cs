using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookIt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resources_Type",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ResourceId_StartUtc_EndUtc",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_IsActive_Name",
                table: "Resources",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAtUtc" },
                filter: "[RevokedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ResourceId_StartUtc_EndUtc",
                table: "Bookings",
                columns: new[] { "ResourceId", "StartUtc", "EndUtc" },
                filter: "[Status] <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resources_IsActive_Name",
                table: "Resources");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId_RevokedAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ResourceId_StartUtc_EndUtc",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_Type",
                table: "Resources",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ResourceId_StartUtc_EndUtc",
                table: "Bookings",
                columns: new[] { "ResourceId", "StartUtc", "EndUtc" });
        }
    }
}
