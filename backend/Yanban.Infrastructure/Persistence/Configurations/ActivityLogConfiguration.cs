using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> b)
    {
        b.ToTable("activity_logs");
        b.HasKey(x => x.Id);

        // Postgres GENERATED ALWAYS AS IDENTITY: the monotonic ordering/outbox cursor.
        // Unique so it can key a keyset scan; the app never supplies it.
        b.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        b.HasAlternateKey(x => x.Sequence);

        // Stored as the enum name (readable straight from SQL, and adding an action
        // later can't silently renumber history the way an int mapping would).
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(20);
        b.Property(x => x.Summary).HasMaxLength(500);

        // The board feed reads newest-first within a board — a descending composite
        // index serves both the filter and the ORDER BY straight from the index.
        b.HasIndex(x => new { x.BoardId, x.Sequence }).IsDescending(false, true);

        // BoardId/ActorId are intentionally unconstrained columns (see ActivityLog):
        // audit rows must survive deletion of the board or user they reference.
    }
}
