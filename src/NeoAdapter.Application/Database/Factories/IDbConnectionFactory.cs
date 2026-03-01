using System.Data.Common;

namespace NeoAdapter.Application.Database.Factories;

public interface IDbConnectionFactory
{
    DbConnection CreateConnection(string connectionString);
}