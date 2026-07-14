using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(x => x.Id);

        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(x => x.RecipientEmail).IsRequired().HasMaxLength(320);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.Property(x => x.LastError).HasMaxLength(1000);

        // The claim query's only access path, and the reason it stays cheap as the table grows:
        // a partial index over the *pending* rows alone. Sent rows are history — millions of them
        // must not make "what is left to send?" any slower.
        b.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasFilter($"status = '{nameof(OutboxStatus.Pending)}'")
            .HasDatabaseName("ix_outbox_messages_pending");

        // RecipientUserId and BoardId are plain columns, not foreign keys — deliberately, like
        // ActivityLog's. A message about a board is worth keeping after the board is gone, and a
        // cascade would delete the very evidence that we mailed someone.
    }
}
