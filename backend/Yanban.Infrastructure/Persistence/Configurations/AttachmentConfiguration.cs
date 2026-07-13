using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments");
        b.HasKey(x => x.Id);

        b.Property(x => x.FileName).IsRequired().HasMaxLength(255);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(255);
        b.Property(x => x.StorageKey).IsRequired().HasMaxLength(512);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        b.HasIndex(x => x.CardId);

        b.HasOne(x => x.Card)
            .WithMany()
            .HasForeignKey(x => x.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.UploadedBy)
            .WithMany()
            .HasForeignKey(x => x.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
