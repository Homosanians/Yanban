using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        b.Property(u => u.Email).IsRequired().HasMaxLength(320);
        b.HasIndex(u => u.Email).IsUnique();

        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        b.Property(u => u.TokenVersion).HasDefaultValue(0);

        // NULLs are distinct in a Postgres unique index, so multiple non-VK users are fine.
        b.HasIndex(u => u.VkId).IsUnique();
    }
}
