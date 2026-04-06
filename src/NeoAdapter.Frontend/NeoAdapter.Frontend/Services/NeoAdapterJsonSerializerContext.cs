using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Frontend.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RegisterUserRequest))]
[JsonSerializable(typeof(ConnectorDto))]
[JsonSerializable(typeof(CreateConnectorRequest))]
[JsonSerializable(typeof(TestConnectorRequest))]
[JsonSerializable(typeof(TestConnectorResponse))]
[JsonSerializable(typeof(IReadOnlyList<ConnectorDto>))]
[JsonSerializable(typeof(DashboardResponse))]
[JsonSerializable(typeof(IntegrationJobDto))]
[JsonSerializable(typeof(CreateIntegrationJobRequest))]
[JsonSerializable(typeof(EnqueueIntegrationJobResponse))]
[JsonSerializable(typeof(IReadOnlyList<IntegrationJobDto>))]
internal partial class NeoAdapterJsonSerializerContext : JsonSerializerContext
{
}

internal static class NeoAdapterJsonTypeInfo
{
    public static JsonTypeInfo<T> For<T>()
    {
        return (JsonTypeInfo<T>)NeoAdapterJsonSerializerContext.Default.GetTypeInfo(typeof(T))!;
    }
}
