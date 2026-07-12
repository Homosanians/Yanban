using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> b)
    {
        b.ToTable("cards");
        b.HasKey(x => x.Id);

        b.Property(x => x.Title).IsRequired().HasMaxLength(500);
        b.Property(x => x.Rank).IsRequired().HasMaxLength(64);
        b.HasIndex(x => new { x.ListId, x.Rank });

        b.HasOne(x => x.List)
            .WithMany(l => l.Cards)
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Assignee)
            .WithMany()
            .HasForeignKey(x => x.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency via Postgres' system column xmin. This is exactly
        // what Npgsql's UseXminAsConcurrencyToken() wraps; mapping it explicitly is
        // provider-identical and the "xid" system column is excluded from DDL.
        b.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
