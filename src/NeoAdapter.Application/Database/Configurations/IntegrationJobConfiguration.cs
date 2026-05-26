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

        builder.Property(job => job.OwnerUserId)
            .HasColumnName("owner_user_id");

        builder.Property(job => job.OwnerGroupId)
            .HasColumnName("owner_group_id");

        builder.Property(job => job.OwnerOrganizationId)
            .HasColumnName("owner_organization_id");

        builder.Property(job => job.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        builder.Property(job => job.CronExpression)
            .HasColumnName("cron_expression")
            .HasMaxLength(120);

        builder.Property(job => job.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(job => job.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(job => job.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(job => job.OwnerGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(job => job.OwnerOrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(job => job.Groups)
            .WithMany(group => group.IntegrationJobs)
            .UsingEntity<Dictionary<string, object>>(
                "IntegrationJobGroup",
                r => r.HasOne<Group>().WithMany().HasForeignKey("group_id"),
                l => l.HasOne<IntegrationJob>().WithMany().HasForeignKey("integration_job_id"),
                je =>
                {
                    je.ToTable("integration_job_groups");
                    je.Property<Guid>("integration_job_id").HasColumnName("integration_job_id");
                    je.Property<Guid>("group_id").HasColumnName("group_id");
                    je.HasKey("integration_job_id", "group_id");
                });
    }
}
