using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_bookings_catalog_request_id",
                table: "bookings",
                column: "catalog_request_id",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "idx_bookings_created_at",
                table: "bookings",
                column: "created_at",
                descending: new bool[0]
            );

            migrationBuilder.CreateIndex(
                name: "idx_bookings_resource_id_dates",
                table: "bookings",
                columns: new[] { "resource_id", "booked_from", "booked_to" }
            );

            migrationBuilder.CreateIndex(
                name: "idx_bookings_user_id_status",
                table: "bookings",
                columns: new[] { "user_id", "status" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "idx_bookings_catalog_request_id", table: "bookings");

            migrationBuilder.DropIndex(name: "idx_bookings_created_at", table: "bookings");

            migrationBuilder.DropIndex(name: "idx_bookings_resource_id_dates", table: "bookings");

            migrationBuilder.DropIndex(name: "idx_bookings_user_id_status", table: "bookings");
        }
    }
}
