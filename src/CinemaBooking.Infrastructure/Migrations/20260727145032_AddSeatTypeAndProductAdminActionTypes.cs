using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CinemaBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatTypeAndProductAdminActionTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdminActionLog_ActionType",
                table: "AdminActionLog");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdminActionLog_ActionType",
                table: "AdminActionLog",
                sql: "[ActionType] IN ('create_showtime_type','update_showtime_type','delete_showtime_type','generate_showtime_by_type','create_user','update_user','lock_user','unlock_user','change_role','delete_user','deactivate_user','create_voucher','update_voucher','delete_voucher','create_showtime','update_showtime','delete_showtime','update_ticket_price','generate_seat','update_seat','delete_seat','create_cinema','update_cinema','delete_cinema','create_genre','update_genre','delete_genre','create_movie','update_movie','delete_movie','create_room_type','update_room_type','delete_room_type','export_report','refund','create_seat_type','update_seat_type','delete_seat_type','create_product','update_product','delete_product')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdminActionLog_ActionType",
                table: "AdminActionLog");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdminActionLog_ActionType",
                table: "AdminActionLog",
                sql: "[ActionType] IN ('create_showtime_type','update_showtime_type','delete_showtime_type','generate_showtime_by_type','create_user','update_user','lock_user','unlock_user','change_role','delete_user','deactivate_user','create_voucher','update_voucher','delete_voucher','create_showtime','update_showtime','delete_showtime','update_ticket_price','generate_seat','update_seat','delete_seat','create_cinema','update_cinema','delete_cinema','create_genre','update_genre','delete_genre','create_movie','update_movie','delete_movie','create_room_type','update_room_type','delete_room_type','export_report','refund')");
        }
    }
}
