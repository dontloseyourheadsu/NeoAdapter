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

        builder.Property(user => user.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(user => user.GoogleId)
            .HasColumnName("google_id")
            .HasMaxLength(100);

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        builder.Property(user => user.GroupId)
            .HasColumnName("group_id");

        builder.Property(user => user.Role)
            .HasColumnName("role")
            .HasMaxLength(20)
            .HasDefaultValue("User")
            .IsRequired();

        builder.Property(user => user.RoleRead)
            .HasColumnName("role_read")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(user => user.RoleEdit)
            .HasColumnName("role_edit")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(user => user.RoleCreate)
            .HasColumnName("role_create")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(user => user.RoleAdmin)
            .HasColumnName("role_admin")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(user => user.LastLoginAtUtc)
            .HasColumnName("last_login_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(user => user.Organization)
            .WithMany(org => org.Users)
            .HasForeignKey(user => user.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(user => user.Group)
            .WithMany(group => group.Members)
            .HasForeignKey(user => user.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
