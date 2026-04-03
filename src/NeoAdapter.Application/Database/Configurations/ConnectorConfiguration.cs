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

        builder.Property(connector => connector.SqlServer)
            .HasColumnName("sql_server")
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

        builder.Property(connector => connector.SqlTable)
            .HasColumnName("sql_table")
            .HasMaxLength(255);

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

        builder.Property(connector => connector.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(connector => connector.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}