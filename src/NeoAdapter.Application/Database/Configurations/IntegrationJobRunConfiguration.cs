using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class IntegrationJobRunConfiguration : IEntityTypeConfiguration<IntegrationJobRun>
{
    public void Configure(EntityTypeBuilder<IntegrationJobRun> builder)
    {
        builder.ToTable("integration_job_runs");

        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id)
            .HasColumnName("id");

        builder.Property(run => run.IntegrationJobId)
            .HasColumnName("integration_job_id")
            .IsRequired();

        builder.Property(run => run.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(run => run.Message)
            .HasColumnName("message")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(run => run.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(run => run.FinishedAtUtc)
            .HasColumnName("finished_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(run => run.RecordsProcessed)
            .HasColumnName("records_processed")
            .IsRequired();

        builder.Property(run => run.HangfireJobId)
            .HasColumnName("hangfire_job_id")
            .HasMaxLength(64);

        builder.HasIndex(run => new { run.IntegrationJobId, run.StartedAtUtc });

        builder.HasOne(run => run.IntegrationJob)
            .WithMany()
            .HasForeignKey(run => run.IntegrationJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}