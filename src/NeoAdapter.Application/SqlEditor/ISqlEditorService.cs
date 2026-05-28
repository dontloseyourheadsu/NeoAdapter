using System;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.SqlEditor;

namespace NeoAdapter.Application.SqlEditor;

public interface ISqlEditorService
{
    Task<SqlSchemaResponse> GetSchemaAsync(
        GetSchemaRequest request,
        Guid userId,
        Guid organizationId,
        Guid? groupId,
        string role,
        bool roleRead,
        bool roleCreate,
        bool roleAdmin,
        CancellationToken cancellationToken);

    Task<QueryResultDto> ExecuteQueryAsync(
        ExecuteQueryRequest request,
        Guid userId,
        Guid organizationId,
        Guid? groupId,
        string role,
        bool roleRead,
        bool roleCreate,
        bool roleAdmin,
        CancellationToken cancellationToken);
}
