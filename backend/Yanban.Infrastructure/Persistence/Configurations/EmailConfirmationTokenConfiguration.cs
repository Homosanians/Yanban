using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class EmailConfirmationTokenConfiguration : IEntityTypeConfiguration<EmailConfirmationToken>
{
    public void Configure(EntityTypeBuilder<EmailConfirmationToken> b)
    {
        b.ToTable("email_confirmation_tokens");
        b.HasKey(x => x.Id);

        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        // Redemption looks the token up by its hash, and by nothing else.
        b.HasIndex(x => x.TokenHash).IsUnique();

        b.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
