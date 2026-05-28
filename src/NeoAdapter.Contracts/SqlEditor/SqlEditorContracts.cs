using NeoAdapter.Contracts.Connectors;
using System;
using System.Collections.Generic;

namespace NeoAdapter.Contracts.SqlEditor;

public sealed record SqlEditorConnectionSettings(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate
);

public sealed record GetSchemaRequest(
    Guid? ConnectorId,
    string? ConnectionString,
    SqlEditorConnectionSettings? ConnectionSettings,
    ConnectorType? Type,
    bool SaveConnection,
    string? ConnectionName
);

public sealed record SqlSchemaObjectDto(string Schema, string Name);

public sealed record SqlSchemaResponse(
    IReadOnlyList<SqlSchemaObjectDto> Tables,
    IReadOnlyList<SqlSchemaObjectDto> Views,
    IReadOnlyList<SqlSchemaObjectDto> Procedures,
    IReadOnlyList<SqlSchemaObjectDto> Functions,
    ConnectorDto? SavedConnector
);

public sealed record ExecuteQueryRequest(
    Guid? ConnectorId,
    string? ConnectionString,
    SqlEditorConnectionSettings? ConnectionSettings,
    ConnectorType? Type,
    string Query,
    bool SaveConnection,
    string? ConnectionName
);

public sealed record QueryResultDto(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RowsAffected,
    string? ErrorMessage = null
);
