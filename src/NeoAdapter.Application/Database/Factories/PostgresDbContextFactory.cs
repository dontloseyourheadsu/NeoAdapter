using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using Npgsql;

namespace NeoAdapter.Application.Database.Factories;

public sealed class PostgresDbContextFactory : IDbConnectionFactory
{
    public DbConnection CreateConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        var options = new DbContextOptionsBuilder<NeoAdapterDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        using var context = new NeoAdapterDbContext(options);
        var dbConnection = context.Database.GetDbConnection();

        if (dbConnection is not NpgsqlConnection npgsqlConnection)
        {
            throw new InvalidOperationException("The configured provider does not expose an Npgsql connection.");
        }

        return new NpgsqlConnection(npgsqlConnection.ConnectionString);
    }
}