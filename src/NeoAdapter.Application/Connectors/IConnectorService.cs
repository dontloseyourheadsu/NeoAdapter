using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Application.Connectors;

public interface IConnectorService
{
    Task<IReadOnlyList<ConnectorDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<ConnectorDto> CreateAsync(CreateConnectorRequest request, CancellationToken cancellationToken);
}