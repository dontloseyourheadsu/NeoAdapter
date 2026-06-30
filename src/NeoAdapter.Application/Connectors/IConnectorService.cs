using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Application.Connectors;

public interface IConnectorService
{
    Task<IReadOnlyList<ConnectorDto>> GetAllAsync(Guid userId, Guid organizationId, Guid? groupId, string role, bool roleRead, bool roleAdmin, CancellationToken cancellationToken);

    Task<ConnectorDto> CreateAsync(CreateConnectorRequest request, Guid userId, Guid organizationId, Guid? groupId, string role, bool roleCreate, bool roleAdmin, CancellationToken cancellationToken);

    Task<TestConnectorResponse> TestAsync(TestConnectorRequest request, Guid userId, CancellationToken cancellationToken);
}