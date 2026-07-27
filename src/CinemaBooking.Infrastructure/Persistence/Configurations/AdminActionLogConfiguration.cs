using CinemaBooking.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CinemaBooking.Infrastructure.Persistence.Configurations;

public class AdminActionLogConfiguration : IEntityTypeConfiguration<AdminActionLog>
{
    public void Configure(EntityTypeBuilder<AdminActionLog> builder)
    {
        builder.ToTable("AdminActionLog");

        builder.HasKey(a => a.LogID);

        builder.Property(a => a.TargetTable).HasMaxLength(50);
        builder.Property(a => a.ActionType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.IPAddress).HasMaxLength(45).IsRequired();
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(a => a.AdminID).HasDatabaseName("IX_AdminActionLog_AdminID");
        builder.HasIndex(a => a.TargetUserID).HasDatabaseName("IX_AdminActionLog_TargetUserID");
        builder.HasIndex(a => a.ActionType);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.TargetTable, a.TargetID });

        builder.HasOne(a => a.Admin)
            .WithMany(u => u.AdminActions)
            .HasForeignKey(a => a.AdminID)
            .HasConstraintName("FK_AdminActionLog_Admin");

        builder.HasOne(a => a.TargetUser)
            .WithMany(u => u.TargetedAdminActions)
            .HasForeignKey(a => a.TargetUserID)
            .HasConstraintName("FK_AdminActionLog_TargetUser");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AdminActionLog_ActionType",
            "[ActionType] IN ('create_showtime_type','update_showtime_type','delete_showtime_type','generate_showtime_by_type','create_user','update_user','lock_user','unlock_user','change_role','delete_user','deactivate_user','create_voucher','update_voucher','delete_voucher','create_showtime','update_showtime','delete_showtime','update_ticket_price','generate_seat','update_seat','delete_seat','create_cinema','update_cinema','delete_cinema','create_genre','update_genre','delete_genre','create_movie','update_movie','delete_movie','create_room_type','update_room_type','delete_room_type','export_report','refund','create_seat_type','update_seat_type','delete_seat_type','create_product','update_product','delete_product')"));
    }
}
