using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("user_accounts");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id)
            .HasColumnName("id");

        builder.Property(user => user.Username)
            .HasColumnName("username")
            .HasMaxLength(80)
            .IsRequired();
        builder.HasIndex(user => user.Username)
            .IsUnique();

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(user => user.PasswordSalt)
            .HasColumnName("password_salt")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc")
            .HasColumnType("timestamp with time zone");
    }
}