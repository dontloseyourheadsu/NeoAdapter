using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id)
            .HasColumnName("id");

        builder.Property(job => job.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(job => job.Source)
            .HasColumnName("source")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(job => job.Target)
            .HasColumnName("target")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(job => job.ColorHex)
            .HasColumnName("color_hex")
            .HasMaxLength(9)
            .IsRequired();

        builder.Property(job => job.Schedule)
            .HasColumnName("schedule")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(job => job.CreatedDate)
            .HasColumnName("created_date")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(job => job.UpdatedDate)
            .HasColumnName("updated_date")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }
}