using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Database.Configurations;

public sealed class ConnectorConfiguration : IEntityTypeConfiguration<Connector>
{
    public void Configure(EntityTypeBuilder<Connector> builder)
    {
        builder.ToTable("connectors");

        builder.HasKey(connector => connector.Id);
        builder.Property(connector => connector.Id)
            .HasColumnName("id");

        builder.Property(connector => connector.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();
        builder.HasIndex(connector => connector.Name)
            .IsUnique();

        builder.Property(connector => connector.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(connector => connector.SqlHost)
            .HasColumnName("sql_host")
            .HasMaxLength(255);

        builder.Property(connector => connector.SqlPort)
            .HasColumnName("sql_port");

        builder.Property(connector => connector.SqlDatabase)
            .HasColumnName("sql_database")
            .HasMaxLength(255);

        builder.Property(connector => connector.SqlUsername)
            .HasColumnName("sql_username")
            .HasMaxLength(255);

        builder.Property(connector => connector.SqlPassword)
            .HasColumnName("sql_password")
            .HasMaxLength(255);

        builder.Property(connector => connector.SqlConfigJson)
            .HasColumnName("sql_config_json")
            .HasColumnType("jsonb");

        builder.Property(connector => connector.SqlTrustServerCertificate)
            .HasColumnName("sql_trust_server_certificate")
            .IsRequired();

        builder.Property(connector => connector.CsvPath)
            .HasColumnName("csv_path")
            .HasMaxLength(1000);

        builder.Property(connector => connector.CsvDelimiter)
            .HasColumnName("csv_delimiter")
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(connector => connector.ExcelPath)
            .HasColumnName("excel_path")
            .HasMaxLength(1000);

        builder.Property(connector => connector.ExcelSheetName)
            .HasColumnName("excel_sheet_name")
            .HasMaxLength(255);

        builder.Property(connector => connector.LocalPath)
            .HasColumnName("local_path")
            .HasMaxLength(1000);

        builder.Property(connector => connector.SftpHost)
            .HasColumnName("sftp_host")
            .HasMaxLength(255);

        builder.Property(connector => connector.SftpPort)
            .HasColumnName("sftp_port");

        builder.Property(connector => connector.SftpUsername)
            .HasColumnName("sftp_username")
            .HasMaxLength(255);

        builder.Property(connector => connector.SftpPassword)
            .HasColumnName("sftp_password")
            .HasMaxLength(255);

        builder.Property(connector => connector.SftpRemotePath)
            .HasColumnName("sftp_remote_path")
            .HasMaxLength(1000);

        builder.Property(connector => connector.SharePointSiteUrl)
            .HasColumnName("sharepoint_site_url")
            .HasMaxLength(1000);

        builder.Property(connector => connector.SharePointListName)
            .HasColumnName("sharepoint_list_name")
            .HasMaxLength(255);

        builder.Property(connector => connector.SharePointConfigJson)
            .HasColumnName("sharepoint_config_json")
            .HasColumnType("jsonb");


        builder.Property(connector => connector.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(connector => connector.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(connector => connector.OwnerUserId)
            .HasColumnName("owner_user_id");

        builder.Property(connector => connector.OwnerGroupId)
            .HasColumnName("owner_group_id");

        builder.Property(connector => connector.OwnerOrganizationId)
            .HasColumnName("owner_organization_id");

        builder.HasOne(connector => connector.OwnerUser)
            .WithMany()
            .HasForeignKey(connector => connector.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(connector => connector.OwnerGroup)
            .WithMany()
            .HasForeignKey(connector => connector.OwnerGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(connector => connector.OwnerOrganization)
            .WithMany()
            .HasForeignKey(connector => connector.OwnerOrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
