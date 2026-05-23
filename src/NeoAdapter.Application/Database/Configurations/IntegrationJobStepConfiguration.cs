using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobStepConfiguration : IEntityTypeConfiguration<IntegrationJobStep>
{
    public void Configure(EntityTypeBuilder<IntegrationJobStep> builder)
    {
        builder.ToTable("integration_job_steps");

        builder.HasKey(step => step.Id);
        builder.Property(step => step.Id)
            .HasColumnName("id");

        builder.Property(step => step.IntegrationJobId)
            .HasColumnName("integration_job_id")
            .IsRequired();

        builder.Property(step => step.OrderIndex)
            .HasColumnName("order_index")
            .IsRequired();

        builder.Property(step => step.SourceConnectorId)
            .HasColumnName("source_connector_id")
            .IsRequired();

        builder.Property(step => step.DestinationConnectorId)
            .HasColumnName("destination_connector_id")
            .IsRequired();

        builder.HasOne(step => step.IntegrationJob)
            .WithMany(job => job.Steps)
            .HasForeignKey(step => step.IntegrationJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(step => step.SourceConnector)
            .WithMany()
            .HasForeignKey(step => step.SourceConnectorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(step => step.DestinationConnector)
            .WithMany()
            .HasForeignKey(step => step.DestinationConnectorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
