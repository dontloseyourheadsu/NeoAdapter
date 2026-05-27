namespace NeoAdapter.Domain;

public sealed class Connector
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ConnectorType Type { get; set; }

    public string? SqlHost { get; set; }

    public int? SqlPort { get; set; }

    public string? SqlDatabase { get; set; }

    public string? SqlUsername { get; set; }

    public string? SqlPassword { get; set; }

    public string? SqlConfigJson { get; set; } // JSON for tables/fields selection

    public bool SqlTrustServerCertificate { get; set; }

    public string? CsvPath { get; set; }

    public string CsvDelimiter { get; set; } = ",";

    public string? ExcelPath { get; set; }

    public string? ExcelSheetName { get; set; }

    public string? LocalPath { get; set; }

    public string? SftpHost { get; set; }

    public int? SftpPort { get; set; }

    public string? SftpUsername { get; set; }

    public string? SftpPassword { get; set; }

    public string? SftpRemotePath { get; set; }


    public Guid? OwnerUserId { get; set; }

    public Guid? OwnerGroupId { get; set; }

    public Guid? OwnerOrganizationId { get; set; }

    public UserAccount? OwnerUser { get; set; }

    public Group? OwnerGroup { get; set; }

    public Organization? OwnerOrganization { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
