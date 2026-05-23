using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");

        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).HasColumnName("id");

        builder.Property(group => group.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(group => group.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(group => group.CreatorUserId)
            .HasColumnName("creator_user_id")
            .IsRequired();

        builder.Property(group => group.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(group => group.Organization)
            .WithMany(org => org.Groups)
            .HasForeignKey(group => group.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(group => group.Creator)
            .WithMany()
            .HasForeignKey(group => group.CreatorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
