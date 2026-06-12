using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobGuestConfiguration : IEntityTypeConfiguration<IntegrationJobGuest>
{
    public void Configure(EntityTypeBuilder<IntegrationJobGuest> builder)
    {
        builder.ToTable("integration_job_guests");

        builder.HasKey(g => new { g.IntegrationJobId, g.UserId });

        builder.Property(g => g.IntegrationJobId)
            .HasColumnName("integration_job_id");

        builder.Property(g => g.UserId)
            .HasColumnName("user_id");

        builder.Property(g => g.CanRead)
            .HasColumnName("can_read")
            .IsRequired();

        builder.Property(g => g.CanEdit)
            .HasColumnName("can_edit")
            .IsRequired();

        builder.Property(g => g.CanCreateConnectors)
            .HasColumnName("can_create_connectors")
            .IsRequired();

        builder.HasOne(g => g.IntegrationJob)
            .WithMany(j => j.Guests)
            .HasForeignKey(g => g.IntegrationJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(g => g.User)
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
