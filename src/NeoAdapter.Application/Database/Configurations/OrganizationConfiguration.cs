using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(org => org.Id);
        builder.Property(org => org.Id).HasColumnName("id");

        builder.Property(org => org.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();
        builder.HasIndex(org => org.Name).IsUnique();

        builder.Property(org => org.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
