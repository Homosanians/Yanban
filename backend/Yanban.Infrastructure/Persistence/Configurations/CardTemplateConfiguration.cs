using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class CardTemplateConfiguration : IEntityTypeConfiguration<CardTemplate>
{
    public void Configure(EntityTypeBuilder<CardTemplate> b)
    {
        b.ToTable("card_templates");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Title).IsRequired().HasMaxLength(500);
        b.HasIndex(x => new { x.BoardId, x.Name });

        b.HasOne(x => x.Board)
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // No search vector: templates are not searchable.
    }
}
