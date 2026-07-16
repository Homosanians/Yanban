using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("notification_preferences");
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();

        // One override per (user, board, type). This index does NOT constrain the global rows:
        // Postgres treats NULLs as distinct, so `board_id IS NULL` duplicates would slip through it.
        b.HasIndex(x => new { x.UserId, x.BoardId, x.Type })
            .IsUnique()
            .HasFilter("board_id IS NOT NULL")
            .HasDatabaseName("ux_notification_preferences_board");

        // ...so the global row gets its own filtered unique index, which is what actually enforces
        // "one global default per user per type".
        b.HasIndex(x => new { x.UserId, x.Type })
            .IsUnique()
            .HasFilter("board_id IS NULL")
            .HasDatabaseName("ux_notification_preferences_global");

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A preference is about a board and dies with it. Unlike an outbox row, there is nothing
        // to keep once the board is gone.
        b.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
