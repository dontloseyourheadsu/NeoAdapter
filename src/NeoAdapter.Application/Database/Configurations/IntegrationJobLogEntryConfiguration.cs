using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobLogEntryConfiguration : IEntityTypeConfiguration<IntegrationJobLogEntry>
{
    public void Configure(EntityTypeBuilder<IntegrationJobLogEntry> builder)
    {
        builder.ToTable("integration_job_logs");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id)
            .HasColumnName("id");

        builder.Property(log => log.IntegrationJobId)
            .HasColumnName("integration_job_id")
            .IsRequired();

        builder.Property(log => log.IntegrationJobRunId)
            .HasColumnName("integration_job_run_id");

        builder.Property(log => log.TimestampUtc)
            .HasColumnName("timestamp_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(log => log.LogLevel)
            .HasColumnName("log_level")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(log => log.Message)
            .HasColumnName("message")
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(log => log.Details)
            .HasColumnName("details")
            .HasColumnType("text");

        builder.HasOne(log => log.IntegrationJob)
            .WithMany()
            .HasForeignKey(log => log.IntegrationJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.IntegrationJobRun)
            .WithMany()
            .HasForeignKey(log => log.IntegrationJobRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(log => new { log.IntegrationJobId, log.TimestampUtc });
    }
}
