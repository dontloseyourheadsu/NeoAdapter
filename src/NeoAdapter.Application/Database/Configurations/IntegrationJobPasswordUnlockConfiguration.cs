using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobPasswordUnlockConfiguration : IEntityTypeConfiguration<IntegrationJobPasswordUnlock>
{
    public void Configure(EntityTypeBuilder<IntegrationJobPasswordUnlock> builder)
    {
        builder.ToTable("integration_job_password_unlocks");

        builder.HasKey(u => new { u.IntegrationJobId, u.UserId });

        builder.Property(u => u.IntegrationJobId)
            .HasColumnName("integration_job_id");

        builder.Property(u => u.UserId)
            .HasColumnName("user_id");

        builder.Property(u => u.UnlockedAtUtc)
            .HasColumnName("unlocked_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(u => u.IntegrationJob)
            .WithMany(j => j.PasswordUnlocks)
            .HasForeignKey(u => u.IntegrationJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
