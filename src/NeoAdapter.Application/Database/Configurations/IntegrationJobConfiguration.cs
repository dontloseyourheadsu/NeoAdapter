using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobConfiguration : IEntityTypeConfiguration<IntegrationJob>
{
    public void Configure(EntityTypeBuilder<IntegrationJob> builder)
    {
        builder.ToTable("integration_jobs");

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id)
            .HasColumnName("id");

        builder.Property(job => job.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();
        builder.HasIndex(job => job.Name)
            .IsUnique();

        builder.Property(job => job.SourceConnectorId)
            .HasColumnName("source_connector_id")
            .IsRequired();

        builder.Property(job => job.DestinationConnectorId)
            .HasColumnName("destination_connector_id")
            .IsRequired();

        builder.Property(job => job.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        builder.Property(job => job.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(job => job.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(job => job.SourceConnector)
            .WithMany()
            .HasForeignKey(job => job.SourceConnectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(job => job.DestinationConnector)
            .WithMany()
            .HasForeignKey(job => job.DestinationConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}