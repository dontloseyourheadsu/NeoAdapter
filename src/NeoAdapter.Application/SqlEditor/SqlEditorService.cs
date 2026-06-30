using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.SqlEditor;
using Npgsql;
using ConnectorType = NeoAdapter.Contracts.Connectors.ConnectorType;

namespace NeoAdapter.Application.SqlEditor;

public sealed class SqlEditorService(
    NeoAdapterDbContext dbContext,
    IConnectorService connectorService,
    ISqlSecretProtector sqlSecretProtector) : ISqlEditorService
{
    public async Task<SqlSchemaResponse> GetSchemaAsync(
        GetSchemaRequest request,
        Guid userId,
        Guid organizationId,
        Guid? groupId,
        string role,
        bool roleRead,
        bool roleCreate,
        bool roleAdmin,
        CancellationToken cancellationToken)
    {
        var (connection, savedConnector) = await GetConnectionAsync(
            request.ConnectorId,
            request.ConnectionString,
            request.ConnectionSettings,
            request.Type,
            request.SaveConnection,
            request.ConnectionName,
            userId,
            organizationId,
            groupId,
            role,
            roleRead,
            roleCreate,
            roleAdmin,
            cancellationToken);

        await using (connection)
        {
            await connection.OpenAsync(cancellationToken);

            var dbType = request.ConnectorId.HasValue 
                ? await GetConnectorTypeAsync(request.ConnectorId.Value, cancellationToken)
                : request.Type!.Value;

            var response = await GetSchemaInternalAsync(connection, dbType, cancellationToken);
            return response with { SavedConnector = savedConnector };
        }
    }

    public async Task<QueryResultDto> ExecuteQueryAsync(
        ExecuteQueryRequest request,
        Guid userId,
        Guid organizationId,
        Guid? groupId,
        string role,
        bool roleRead,
        bool roleCreate,
        bool roleAdmin,
        CancellationToken cancellationToken)
    {
        // 1. Safety validation first
        if (!SqlValidation.IsQueryAllowed(request.Query, out var forbiddenKeyword))
        {
            return new QueryResultDto(
                Columns: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowsAffected: -1,
                ErrorMessage: $"Execution blocked: query contains forbidden keyword '{forbiddenKeyword}'."
            );
        }

        var (connection, savedConnector) = await GetConnectionAsync(
            request.ConnectorId,
            request.ConnectionString,
            request.ConnectionSettings,
            request.Type,
            request.SaveConnection,
            request.ConnectionName,
            userId,
            organizationId,
            groupId,
            role,
            roleRead,
            roleCreate,
            roleAdmin,
            cancellationToken);

        await using (connection)
        {
            await connection.OpenAsync(cancellationToken);

            if (request.ExplainOnly)
            {
                var dbType = request.ConnectorId.HasValue
                    ? await GetConnectorTypeAsync(request.ConnectorId.Value, cancellationToken)
                    : request.Type!.Value;
                return await ExecuteExplainQueryAsync(connection, dbType, request.Query, cancellationToken);
            }

            return await ExecuteQueryInternalAsync(connection, request.Query, cancellationToken);
        }
    }

    private async Task<QueryResultDto> ExecuteExplainQueryAsync(
        DbConnection connection,
        ConnectorType type,
        string query,
        CancellationToken cancellationToken)
    {
        return type switch
        {
            ConnectorType.Postgres => await ExecutePostgresExplainAsync(connection, query, cancellationToken),
            ConnectorType.SqlServer => await ExecuteSqlServerExplainAsync(connection, query, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported database type for explain query.")
        };
    }

    private async Task<QueryResultDto> ExecutePostgresExplainAsync(
        DbConnection connection,
        string query,
        CancellationToken cancellationToken)
    {
        // Placeholder to be implemented in subsequent commits
        await Task.CompletedTask;
        return new QueryResultDto(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>(), 0);
    }

    private async Task<QueryResultDto> ExecuteSqlServerExplainAsync(
        DbConnection connection,
        string query,
        CancellationToken cancellationToken)
    {
        // Placeholder to be implemented in subsequent commits
        await Task.CompletedTask;
        return new QueryResultDto(Array.Empty<string>(), Array.Empty<IReadOnlyList<object?>>(), 0);
    }

    private async Task<ConnectorType> GetConnectorTypeAsync(Guid connectorId, CancellationToken cancellationToken)
    {
        var connector = await dbContext.Connectors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectorId, cancellationToken);
        if (connector == null)
        {
            throw new InvalidOperationException("Connector not found.");
        }

        return connector.Type switch
        {
            Domain.ConnectorType.SqlServer => ConnectorType.SqlServer,
            Domain.ConnectorType.Postgres => ConnectorType.Postgres,
            _ => throw new InvalidOperationException("Unsupported database connector type.")
        };
    }

    private async Task<(DbConnection Connection, ConnectorDto? SavedConnector)> GetConnectionAsync(
        Guid? connectorId,
        string? connectionString,
        SqlEditorConnectionSettings? connectionSettings,
        ConnectorType? type,
        bool saveConnection,
        string? connectionName,
        Guid userId,
        Guid organizationId,
        Guid? groupId,
        string role,
        bool roleRead,
        bool roleCreate,
        bool roleAdmin,
        CancellationToken cancellationToken)
    {
        ConnectorDto? savedConnector = null;

        // Case 1: Stored Connector
        if (connectorId.HasValue)
        {
            var connector = await dbContext.Connectors
                .FirstOrDefaultAsync(c => c.Id == connectorId.Value, cancellationToken);
            if (connector == null)
            {
                throw new InvalidOperationException("Connector not found.");
            }

            // Verify permissions
            bool isAllowed;
            if (roleAdmin || role == "Admin")
            {
                isAllowed = connector.OwnerOrganizationId == organizationId || connector.OwnerOrganizationId == null;
            }
            else
            {
                isAllowed = (connector.OwnerOrganizationId == organizationId || connector.OwnerOrganizationId == null) &&
                            (connector.OwnerGroupId == null || connector.OwnerGroupId == groupId || connector.OwnerUserId == userId);
            }

            if (!isAllowed)
            {
                throw new UnauthorizedAccessException("You do not have access to this connector.");
            }

            if (connector.Type != Domain.ConnectorType.SqlServer && connector.Type != Domain.ConnectorType.Postgres)
            {
                throw new InvalidOperationException("Selected connector is not a SQL database connector.");
            }

            string connStr;
            if (connector.Type == Domain.ConnectorType.SqlServer)
            {
                var sqlSettings = new SqlConnectorSettingsInputDto(
                    connector.SqlHost ?? string.Empty,
                    connector.SqlPort ?? 1433,
                    connector.SqlDatabase ?? string.Empty,
                    connector.SqlUsername ?? string.Empty,
                    sqlSecretProtector.Unprotect(connector.SqlPassword ?? string.Empty),
                    connector.SqlTrustServerCertificate
                );
                connStr = BuildSqlServerConnectionString(new SqlEditorConnectionSettings(
                    sqlSettings.Host, sqlSettings.Port, sqlSettings.Database, sqlSettings.Username, sqlSettings.Password, sqlSettings.TrustServerCertificate));
            }
            else
            {
                var sqlSettings = new SqlConnectorSettingsInputDto(
                    connector.SqlHost ?? string.Empty,
                    connector.SqlPort ?? 5432,
                    connector.SqlDatabase ?? string.Empty,
                    connector.SqlUsername ?? string.Empty,
                    sqlSecretProtector.Unprotect(connector.SqlPassword ?? string.Empty),
                    connector.SqlTrustServerCertificate
                );
                connStr = BuildPostgresConnectionString(new SqlEditorConnectionSettings(
                    sqlSettings.Host, sqlSettings.Port, sqlSettings.Database, sqlSettings.Username, sqlSettings.Password, sqlSettings.TrustServerCertificate));
            }

            var dbType = connector.Type == Domain.ConnectorType.SqlServer ? ConnectorType.SqlServer : ConnectorType.Postgres;
            var conn = CreateConnection(dbType, connStr);
            return (conn, null);
        }

        // Case 2: Manual settings or Raw Connection String
        if (!type.HasValue)
        {
            throw new InvalidOperationException("Database type (SqlServer or Postgres) is required for manual connections.");
        }

        string finalConnStr;
        SqlEditorConnectionSettings finalSettings;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            finalConnStr = connectionString;
            // Parse connection string to get settings for potential saving
            var parsedSettingsInput = ParseConnectionString(connectionString, type.Value);
            finalSettings = new SqlEditorConnectionSettings(
                parsedSettingsInput.Host,
                parsedSettingsInput.Port,
                parsedSettingsInput.Database,
                parsedSettingsInput.Username,
                parsedSettingsInput.Password,
                parsedSettingsInput.TrustServerCertificate
            );
        }
        else if (connectionSettings != null)
        {
            finalSettings = connectionSettings;
            finalConnStr = type.Value == ConnectorType.SqlServer
                ? BuildSqlServerConnectionString(connectionSettings)
                : BuildPostgresConnectionString(connectionSettings);
        }
        else
        {
            throw new InvalidOperationException("Connection settings or connection string must be provided.");
        }

        if (saveConnection)
        {
            var name = connectionName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"SQL Editor {type.Value} {finalSettings.Database}";
            }

            var createRequest = new CreateConnectorRequest(
                name,
                type.Value,
                new SqlConnectorSettingsInputDto(
                    finalSettings.Host,
                    finalSettings.Port,
                    finalSettings.Database,
                    finalSettings.Username,
                    finalSettings.Password,
                    finalSettings.TrustServerCertificate
                ),
                null, null, null, null
            );

            savedConnector = await connectorService.CreateAsync(
                createRequest, userId, organizationId, groupId, role, roleCreate, roleAdmin, cancellationToken);
        }

        var connection = CreateConnection(type.Value, finalConnStr);
        return (connection, savedConnector);
    }

    private DbConnection CreateConnection(ConnectorType type, string connectionString)
    {
        return type switch
        {
            ConnectorType.SqlServer => new SqlConnection(connectionString),
            ConnectorType.Postgres => new NpgsqlConnection(connectionString),
            _ => throw new InvalidOperationException("Unsupported SQL database type.")
        };
    }

    private string BuildSqlServerConnectionString(SqlEditorConnectionSettings sql)
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

    private string BuildPostgresConnectionString(SqlEditorConnectionSettings sql)
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

    private static SqlConnectorSettingsInputDto ParseConnectionString(string connectionString, ConnectorType type)
    {
        if (type == ConnectorType.SqlServer)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var host = builder.DataSource;
            int port = 1433;
            if (host.Contains(','))
            {
                var parts = host.Split(',');
                host = parts[0].Trim();
                if (parts.Length > 1 && int.TryParse(parts[1], out var parsedPort))
                {
                    port = parsedPort;
                }
            }
            return new SqlConnectorSettingsInputDto(
                Host: host,
                Port: port,
                Database: builder.InitialCatalog,
                Username: builder.UserID,
                Password: builder.Password,
                TrustServerCertificate: builder.TrustServerCertificate
            );
        }
        else if (type == ConnectorType.Postgres)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return new SqlConnectorSettingsInputDto(
                Host: builder.Host ?? string.Empty,
                Port: builder.Port > 0 ? builder.Port : 5432,
                Database: builder.Database ?? string.Empty,
                Username: builder.Username ?? string.Empty,
                Password: builder.Password ?? string.Empty,
                TrustServerCertificate: builder.TrustServerCertificate
            );
        }
        else
        {
            throw new InvalidOperationException("Unsupported database type for parsing connection string.");
        }
    }

    private async Task<SqlSchemaResponse> GetSchemaInternalAsync(DbConnection connection, ConnectorType type, CancellationToken cancellationToken)
    {
        // 1. Get Tables
        string tablesQuery = type switch
        {
            ConnectorType.SqlServer => 
                "SELECT TABLE_SCHEMA AS [Schema], TABLE_NAME AS [Name] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME;",
            ConnectorType.Postgres => 
                "SELECT table_schema AS \"Schema\", table_name AS \"Name\" FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema, table_name;",
            _ => throw new InvalidOperationException("Unsupported SQL type.")
        };
        var tables = await QuerySchemaObjectsAsync(connection, tablesQuery, cancellationToken);

        // 2. Get Views
        string viewsQuery = type switch
        {
            ConnectorType.SqlServer => 
                "SELECT TABLE_SCHEMA AS [Schema], TABLE_NAME AS [Name] FROM INFORMATION_SCHEMA.VIEWS ORDER BY TABLE_SCHEMA, TABLE_NAME;",
            ConnectorType.Postgres => 
                "SELECT table_schema AS \"Schema\", table_name AS \"Name\" FROM information_schema.views WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema, table_name;",
            _ => throw new InvalidOperationException("Unsupported SQL type.")
        };
        var views = await QuerySchemaObjectsAsync(connection, viewsQuery, cancellationToken);

        // 3. Get Procedures
        string proceduresQuery = type switch
        {
            ConnectorType.SqlServer => 
                "SELECT ROUTINE_SCHEMA AS [Schema], ROUTINE_NAME AS [Name] FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;",
            ConnectorType.Postgres => 
                "SELECT routine_schema AS \"Schema\", routine_name AS \"Name\" FROM information_schema.routines WHERE routine_type = 'PROCEDURE' AND routine_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY routine_schema, routine_name;",
            _ => throw new InvalidOperationException("Unsupported SQL type.")
        };
        var procedures = await QuerySchemaObjectsAsync(connection, proceduresQuery, cancellationToken);

        // 4. Get Functions
        string functionsQuery = type switch
        {
            ConnectorType.SqlServer => 
                "SELECT ROUTINE_SCHEMA AS [Schema], ROUTINE_NAME AS [Name] FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'FUNCTION' ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME;",
            ConnectorType.Postgres => 
                "SELECT routine_schema AS \"Schema\", routine_name AS \"Name\" FROM information_schema.routines WHERE routine_type = 'FUNCTION' AND routine_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY routine_schema, routine_name;",
            _ => throw new InvalidOperationException("Unsupported SQL type.")
        };
        var functions = await QuerySchemaObjectsAsync(connection, functionsQuery, cancellationToken);

        return new SqlSchemaResponse(tables, views, procedures, functions, null);
    }

    private async Task<List<SqlSchemaObjectDto>> QuerySchemaObjectsAsync(DbConnection connection, string query, CancellationToken cancellationToken)
    {
        var list = new List<SqlSchemaObjectDto>();
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var name = reader.GetString(1);
            list.Add(new SqlSchemaObjectDto(schema, name));
        }
        return list;
    }

    private async Task<QueryResultDto> ExecuteQueryInternalAsync(DbConnection connection, string query, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<List<object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new List<object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row.Add(val == DBNull.Value ? null : val);
                }
                rows.Add(row);
            }

            return new QueryResultDto(
                Columns: columns,
                Rows: rows,
                RowsAffected: reader.RecordsAffected
            );
        }
        catch (Exception ex)
        {
            return new QueryResultDto(
                Columns: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyList<object?>>(),
                RowsAffected: -1,
                ErrorMessage: ex.Message
            );
        }
    }
}
