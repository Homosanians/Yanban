using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class ObjectDeletionConfiguration : IEntityTypeConfiguration<ObjectDeletion>
{
    public void Configure(EntityTypeBuilder<ObjectDeletion> b)
    {
        b.ToTable("object_deletions");

        // A bigserial identity. This is a work queue keyed by arrival, not a domain entity with a
        // meaningful id, and the trigger inserts by column, so the app never supplies it.
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityAlwaysColumn();

        b.Property(x => x.StorageKey).IsRequired().HasMaxLength(512);
        b.Property(x => x.LastError).HasMaxLength(1000);

        // The claim query's access path: the objects still to delete, oldest first. Partial, so the
        // drained history never slows down "what is left?".
        b.HasIndex(x => x.EnqueuedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_object_deletions_pending");
    }
}
