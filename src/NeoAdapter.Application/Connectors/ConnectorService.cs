using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Domain;
using Npgsql;
using ConnectorTypeContract = NeoAdapter.Contracts.Connectors.ConnectorType;
using ConnectorTypeDomain = NeoAdapter.Domain.ConnectorType;

namespace NeoAdapter.Application.Connectors;

public sealed class ConnectorService(
    NeoAdapterDbContext dbContext,
    ISqlSecretProtector sqlSecretProtector,
    ISharePointApiClient sharePointApiClient) : IConnectorService
{
    private class SqlTableConfig
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Fields { get; set; } = new();
    }

    private class SqlConfig
    {
        public List<SqlTableConfig> Tables { get; set; } = new();
    }

    public async Task<IReadOnlyList<ConnectorDto>> GetAllAsync(Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken)
    {
        if (!roleRead && !roleAdmin)
        {
            throw new UnauthorizedAccessException("You do not have permission to view connectors.");
        }

        IQueryable<Connector> query = dbContext.Connectors.AsNoTracking();

        if (roleAdmin || role == "Admin")
        {
            query = query.Where(c => c.OwnerOrganizationId == organizationId || c.OwnerOrganizationId == null);
        }
        else
        {
            query = query.Where(c => 
                (c.OwnerOrganizationId == organizationId || c.OwnerOrganizationId == null) &&
                (c.OwnerGroupId == null || c.OwnerGroupId == groupId || c.OwnerUserId == userId));
        }

        var connectors = await query
            .OrderBy(connector => connector.Name)
            .ToListAsync(cancellationToken);

        return connectors.Select(MapToDto).ToArray();
    }

    public async Task<ConnectorDto> CreateAsync(CreateConnectorRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleCreate, bool roleAdmin, CancellationToken cancellationToken)
    {
        var hasGuestCreatePermission = await dbContext.Set<IntegrationJobGuest>()
            .AnyAsync(g => g.UserId == userId && g.CanCreateConnectors, cancellationToken);

        if (!roleCreate && !roleAdmin && !hasGuestCreatePermission)
        {
            throw new UnauthorizedAccessException("You do not have permission to create connectors.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var trimmedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Connector name is required.");
        }

        var nameExists = await dbContext.Connectors
            .AnyAsync(connector => connector.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("A connector with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var connector = new Connector
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            Type = MapToDomainType(request.Type),
            OwnerUserId = userId,
            OwnerGroupId = groupId,
            OwnerOrganizationId = organizationId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (request.Type == ConnectorTypeContract.SqlServer || request.Type == ConnectorTypeContract.Postgres)
        {
            if (request.Sql is null)
            {
                throw new InvalidOperationException("SQL connector settings are required.");
            }

            ValidateSqlSettings(request.Sql);
            connector.SqlHost = request.Sql.Host.Trim();
            connector.SqlPort = request.Sql.Port;
            connector.SqlDatabase = request.Sql.Database.Trim();
            connector.SqlUsername = request.Sql.Username.Trim();
            connector.SqlPassword = sqlSecretProtector.Protect(request.Sql.Password);
            connector.SqlTrustServerCertificate = request.Sql.TrustServerCertificate;
            connector.SqlConfigJson = request.Sql.ConfigJson;
            
            if (!string.IsNullOrWhiteSpace(connector.SqlConfigJson))
            {
                await ValidateFkIntegrityAsync(request.Type, request.Sql, cancellationToken);
            }
        }
        else if (request.Type == ConnectorTypeContract.Csv)
        {
            if (request.Csv is null)
            {
                throw new InvalidOperationException("CSV connector settings are required.");
            }

            ValidateCsvSettings(request.Csv);
            connector.CsvPath = request.Csv.Path.Trim();
            connector.CsvDelimiter = request.Csv.Delimiter;
        }
        else if (request.Type == ConnectorTypeContract.Excel)
        {
            if (request.Excel is null)
            {
                throw new InvalidOperationException("Excel connector settings are required.");
            }

            ValidateExcelSettings(request.Excel);
            connector.ExcelPath = request.Excel.Path.Trim();
            connector.ExcelSheetName = request.Excel.SheetName?.Trim();
        }
        else if (request.Type == ConnectorTypeContract.Path)
        {
            if (request.Path is null)
            {
                throw new InvalidOperationException("Path connector settings are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Path.Path))
            {
                throw new InvalidOperationException("Path is required.");
            }

            connector.LocalPath = request.Path.Path.Trim();
        }
        else if (request.Type == ConnectorTypeContract.Sftp)
        {
            if (request.Sftp is null)
            {
                throw new InvalidOperationException("SFTP connector settings are required.");
            }

            ValidateSftpSettings(request.Sftp);
            connector.SftpHost = request.Sftp.Host.Trim();
            connector.SftpPort = request.Sftp.Port;
            connector.SftpUsername = request.Sftp.Username.Trim();
            connector.SftpPassword = sqlSecretProtector.Protect(request.Sftp.Password);
            connector.SftpRemotePath = request.Sftp.RemotePath.Trim();
        }
        else if (request.Type == ConnectorTypeContract.SharePoint)
        {
            var user = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null || string.IsNullOrEmpty(user.MicrosoftId))
            {
                throw new InvalidOperationException("Creating a SharePoint connector requires that your account be authenticated with Microsoft.");
            }

            if (request.SharePoint is null)
            {
                throw new InvalidOperationException("SharePoint connector settings are required.");
            }

            if (string.IsNullOrWhiteSpace(request.SharePoint.SiteUrl))
            {
                throw new InvalidOperationException("SharePoint Site URL is required.");
            }

            if (string.IsNullOrWhiteSpace(request.SharePoint.ListName))
            {
                throw new InvalidOperationException("SharePoint List Name is required.");
            }

            connector.SharePointSiteUrl = request.SharePoint.SiteUrl.Trim();
            connector.SharePointListName = request.SharePoint.ListName.Trim();
            connector.SharePointConfigJson = request.SharePoint.ConfigJson;
        }
        else
        {
            throw new InvalidOperationException("Unsupported connector type.");
        }

        dbContext.Connectors.Add(connector);
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToDto(connector);
    }

    private async Task ValidateFkIntegrityAsync(ConnectorTypeContract type, SqlConnectorSettingsInputDto sql, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql.ConfigJson)) return;

        var config = JsonSerializer.Deserialize<SqlConfig>(sql.ConfigJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (config == null || config.Tables.Count == 0) return;

        await using var connection = CreateConnection(type, sql);
        await connection.OpenAsync(cancellationToken);

        foreach (var table in config.Tables)
        {
            var fks = await GetForeignKeysAsync(connection, type, table.Name);
            foreach (var fk in fks)
            {
                // If the FK column is selected for migration
                if (table.Fields.Contains(fk.ColumnName, StringComparer.OrdinalIgnoreCase))
                {
                    // Check if target table is included
                    var targetTableConfig = config.Tables.FirstOrDefault(t => string.Equals(t.Name, fk.ReferencedTable, StringComparison.OrdinalIgnoreCase));
                    if (targetTableConfig == null)
                    {
                        throw new InvalidOperationException($"Table '{table.Name}' has a Foreign Key on '{fk.ColumnName}' referencing '{fk.ReferencedTable}'. You must include table '{fk.ReferencedTable}' in the connector.");
                    }

                    // Check if target field is included
                    if (!targetTableConfig.Fields.Contains(fk.ReferencedColumn, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Table '{table.Name}' references '{fk.ReferencedTable}({fk.ReferencedColumn})'. You must include field '{fk.ReferencedColumn}' in table '{fk.ReferencedTable}'.");
                    }
                }
            }
        }
    }

    private class ForeignKeyInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string ReferencedTable { get; set; } = string.Empty;
        public string ReferencedColumn { get; set; } = string.Empty;
    }

    private async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(DbConnection connection, ConnectorTypeContract type, string tableName)
    {
        var result = new List<ForeignKeyInfo>();
        await using var command = connection.CreateCommand();

        if (type == ConnectorTypeContract.SqlServer)
        {
            command.CommandText = @"
                SELECT 
                    cc.name AS ColumnName,
                    rt.name AS ReferencedTable,
                    rc.name AS ReferencedColumn
                FROM sys.foreign_key_columns fk
                JOIN sys.tables st ON fk.parent_object_id = st.object_id
                JOIN sys.columns cc ON fk.parent_object_id = cc.object_id AND fk.parent_column_id = cc.column_id
                JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
                JOIN sys.columns rc ON fk.referenced_object_id = rc.object_id AND fk.referenced_column_id = rc.column_id
                WHERE st.name = @tableName";
        }
        else // Postgres
        {
            command.CommandText = @"
                SELECT
                    kcu.column_name AS ColumnName,
                    ccu.table_name AS ReferencedTable,
                    ccu.column_name AS ReferencedColumn
                FROM information_schema.table_constraints AS tc
                JOIN information_schema.key_column_usage AS kcu
                  ON tc.constraint_name = kcu.constraint_name
                  AND tc.table_schema = kcu.table_schema
                JOIN information_schema.constraint_column_usage AS ccu
                  ON ccu.constraint_name = tc.constraint_name
                  AND ccu.table_schema = tc.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_name = @tableName";
        }

        var param = command.CreateParameter();
        param.ParameterName = "@tableName";
        param.Value = tableName;
        command.Parameters.Add(param);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ForeignKeyInfo
            {
                ColumnName = reader.GetString(0),
                ReferencedTable = reader.GetString(1),
                ReferencedColumn = reader.GetString(2)
            });
        }

        return result;
    }

    public async Task<TestConnectorResponse> TestAsync(TestConnectorRequest request, Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Type == ConnectorTypeContract.SqlServer || request.Type == ConnectorTypeContract.Postgres)
        {
            if (request.Sql is null)
            {
                throw new InvalidOperationException("SQL connector settings are required.");
            }

            ValidateSqlSettings(request.Sql);
            
            try
            {
                await using var connection = CreateConnection(request.Type, request.Sql);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(cancellationToken);
                return new TestConnectorResponse(true, $"{request.Type} connection test succeeded.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"Connection test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else if (request.Type == ConnectorTypeContract.Csv)
        {
            if (request.Csv is null)
            {
                throw new InvalidOperationException("CSV connector settings are required.");
            }

            ValidateCsvSettings(request.Csv);
            var path = request.Csv.Path.Trim();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                await using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    await stream.FlushAsync(cancellationToken);
                }
                return new TestConnectorResponse(true, "CSV path test succeeded.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"CSV test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else if (request.Type == ConnectorTypeContract.Excel)
        {
            if (request.Excel is null)
            {
                throw new InvalidOperationException("Excel connector settings are required.");
            }

            ValidateExcelSettings(request.Excel);
            var path = request.Excel.Path.Trim();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                await using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    await stream.FlushAsync(cancellationToken);
                }
                return new TestConnectorResponse(true, "Excel path test succeeded.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"Excel test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else if (request.Type == ConnectorTypeContract.Path)
        {
            if (request.Path is null)
            {
                throw new InvalidOperationException("Path connector settings are required.");
            }

            if (string.IsNullOrWhiteSpace(request.Path.Path))
            {
                throw new InvalidOperationException("Path is required.");
            }

            var path = request.Path.Path.Trim();
            try
            {
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }
                return new TestConnectorResponse(true, "Path verification test succeeded.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"Path verification test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else if (request.Type == ConnectorTypeContract.Sftp)
        {
            if (request.Sftp is null)
            {
                throw new InvalidOperationException("SFTP connector settings are required.");
            }

            ValidateSftpSettings(request.Sftp);
            try
            {
                using var client = new Renci.SshNet.SftpClient(request.Sftp.Host, request.Sftp.Port, request.Sftp.Username, request.Sftp.Password);
                client.Connect();
                if (client.IsConnected)
                {
                    client.Disconnect();
                    return new TestConnectorResponse(true, "SFTP connection test succeeded.", DateTimeOffset.UtcNow);
                }
                return new TestConnectorResponse(false, "SFTP connection test failed: Unknown reason.", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"SFTP connection test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else if (request.Type == ConnectorTypeContract.SharePoint)
        {
            if (request.SharePoint is null)
            {
                throw new InvalidOperationException("SharePoint connector settings are required.");
            }

            var user = await dbContext.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null || string.IsNullOrEmpty(user.MicrosoftId))
            {
                throw new InvalidOperationException("Testing a SharePoint connector requires that your account be authenticated with Microsoft.");
            }

            try
            {
                var token = await sharePointApiClient.GetAccessTokenAsync(request.SharePoint.SiteUrl, cancellationToken);
                var lists = await sharePointApiClient.GetListsAsync(request.SharePoint.SiteUrl, token, cancellationToken);
                return new TestConnectorResponse(true, $"SharePoint connection succeeded. Found {lists.Count} list(s).", DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                return new TestConnectorResponse(false, $"SharePoint connection test failed: {ex.Message}", DateTimeOffset.UtcNow);
            }
        }
        else
        {
            throw new InvalidOperationException("Unsupported connector type.");
        }
    }

    private DbConnection CreateConnection(ConnectorTypeContract type, SqlConnectorSettingsInputDto sql)
    {
        return type switch
        {
            ConnectorTypeContract.SqlServer => new SqlConnection(BuildSqlServerConnectionString(sql)),
            ConnectorTypeContract.Postgres => new NpgsqlConnection(BuildPostgresConnectionString(sql)),
            _ => throw new InvalidOperationException("Unsupported SQL type.")
        };
    }

    private static void ValidateSqlSettings(SqlConnectorSettingsInputDto sql)
    {
        if (string.IsNullOrWhiteSpace(sql.Host)
            || string.IsNullOrWhiteSpace(sql.Database)
            || string.IsNullOrWhiteSpace(sql.Username)
            || string.IsNullOrWhiteSpace(sql.Password))
        {
            throw new InvalidOperationException("SQL connector requires host, database, username, and password.");
        }

        if (sql.Port <= 0)
        {
            throw new InvalidOperationException("SQL connector port must be greater than zero.");
        }
    }

    private static void ValidateCsvSettings(CsvConnectorSettingsDto csv)
    {
        if (string.IsNullOrWhiteSpace(csv.Path))
        {
            throw new InvalidOperationException("CSV connector path is required.");
        }

        if (string.IsNullOrEmpty(csv.Delimiter))
        {
            throw new InvalidOperationException("CSV delimiter is required.");
        }
    }

    private static void ValidateExcelSettings(ExcelConnectorSettingsDto excel)
    {
        if (string.IsNullOrWhiteSpace(excel.Path))
        {
            throw new InvalidOperationException("Excel connector path is required.");
        }
    }

    private ConnectorDto MapToDto(Connector connector)
    {
        SqlConnectorSettingsDto? sql = null;
        CsvConnectorSettingsDto? csv = null;
        ExcelConnectorSettingsDto? excel = null;

        if (connector.Type == ConnectorTypeDomain.SqlServer || connector.Type == ConnectorTypeDomain.Postgres)
        {
            sql = new SqlConnectorSettingsDto(
                connector.SqlHost ?? string.Empty,
                connector.SqlPort ?? (connector.Type == ConnectorTypeDomain.SqlServer ? 1433 : 5432),
                connector.SqlDatabase ?? string.Empty,
                connector.SqlUsername ?? string.Empty,
                connector.SqlTrustServerCertificate,
                connector.SqlConfigJson);
        }

        if (connector.Type == ConnectorTypeDomain.Csv)
        {
            csv = new CsvConnectorSettingsDto(
                connector.CsvPath ?? string.Empty,
                connector.CsvDelimiter);
        }

        if (connector.Type == ConnectorTypeDomain.Excel)
        {
            excel = new ExcelConnectorSettingsDto(
                connector.ExcelPath ?? string.Empty,
                connector.ExcelSheetName);
        }

        PathConnectorSettingsDto? pathSettings = null;
        if (connector.Type == ConnectorTypeDomain.Path)
        {
            pathSettings = new PathConnectorSettingsDto(connector.LocalPath ?? string.Empty);
        }

        SftpConnectorSettingsDto? sftpSettings = null;
        if (connector.Type == ConnectorTypeDomain.Sftp)
        {
            sftpSettings = new SftpConnectorSettingsDto(
                connector.SftpHost ?? string.Empty,
                connector.SftpPort ?? 22,
                connector.SftpUsername ?? string.Empty,
                connector.SftpRemotePath ?? string.Empty);
        }

        SharePointConnectorSettingsDto? sharepointSettings = null;
        if (connector.Type == ConnectorTypeDomain.SharePoint)
        {
            sharepointSettings = new SharePointConnectorSettingsDto(
                connector.SharePointSiteUrl ?? string.Empty,
                connector.SharePointListName ?? string.Empty,
                connector.SharePointConfigJson);
        }

        return new ConnectorDto(
            connector.Id,
            connector.Name,
            MapToContractType(connector.Type),
            sql,
            csv,
            excel,
            pathSettings,
            sftpSettings,
            sharepointSettings,
            connector.CreatedAtUtc,
            connector.UpdatedAtUtc);
    }

    private static ConnectorTypeDomain MapToDomainType(ConnectorTypeContract type) => type switch
    {
        ConnectorTypeContract.SqlServer => ConnectorTypeDomain.SqlServer,
        ConnectorTypeContract.Postgres => ConnectorTypeDomain.Postgres,
        ConnectorTypeContract.Csv => ConnectorTypeDomain.Csv,
        ConnectorTypeContract.Excel => ConnectorTypeDomain.Excel,
        ConnectorTypeContract.Path => ConnectorTypeDomain.Path,
        ConnectorTypeContract.Sftp => ConnectorTypeDomain.Sftp,
        ConnectorTypeContract.SharePoint => ConnectorTypeDomain.SharePoint,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static ConnectorTypeContract MapToContractType(ConnectorTypeDomain type) => type switch
    {
        ConnectorTypeDomain.SqlServer => ConnectorTypeContract.SqlServer,
        ConnectorTypeDomain.Postgres => ConnectorTypeContract.Postgres,
        ConnectorTypeDomain.Csv => ConnectorTypeContract.Csv,
        ConnectorTypeDomain.Excel => ConnectorTypeContract.Excel,
        ConnectorTypeDomain.Path => ConnectorTypeContract.Path,
        ConnectorTypeDomain.Sftp => ConnectorTypeContract.Sftp,
        ConnectorTypeDomain.SharePoint => ConnectorTypeContract.SharePoint,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static void ValidateSftpSettings(SftpConnectorSettingsInputDto sftp)
    {
        if (string.IsNullOrWhiteSpace(sftp.Host)
            || string.IsNullOrWhiteSpace(sftp.Username)
            || string.IsNullOrWhiteSpace(sftp.Password)
            || string.IsNullOrWhiteSpace(sftp.RemotePath))
        {
            throw new InvalidOperationException("SFTP connector requires host, username, password, and remote path.");
        }

        if (sftp.Port <= 0)
        {
            throw new InvalidOperationException("SFTP connector port must be greater than zero.");
        }
    }

    private string BuildSqlServerConnectionString(SqlConnectorSettingsInputDto sql)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{sql.Host},{sql.Port}",
            InitialCatalog = sql.Database,
            UserID = sql.Username,
            Password = sql.Password,
            Encrypt = true,
            TrustServerCertificate = sql.TrustServerCertificate,
            IntegratedSecurity = false,
            ConnectTimeout = 20
        };

        return builder.ConnectionString;
    }

    private string BuildPostgresConnectionString(SqlConnectorSettingsInputDto sql)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = sql.Host,
            Port = sql.Port,
            Database = sql.Database,
            Username = sql.Username,
            Password = sql.Password,
            TrustServerCertificate = sql.TrustServerCertificate,
            Timeout = 20
        };

        return builder.ConnectionString;
    }
}
