using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace NeoAdapter.DbInitializer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Starting Database Initializer...");

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        string? postgresConnString = configuration.GetConnectionString("PostgresTest");
        string? sqlServerConnString = configuration.GetConnectionString("SqlServerTest");

        if (string.IsNullOrEmpty(postgresConnString))
        {
            Console.WriteLine("Warning: ConnectionStrings:PostgresTest environment variable is not set.");
        }
        if (string.IsNullOrEmpty(sqlServerConnString))
        {
            Console.WriteLine("Warning: ConnectionStrings:SqlServerTest environment variable is not set.");
        }

        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var pgTask = Task.Run(() => InitializePostgresAsync(postgresConnString, cts.Token));
        var sqlTask = Task.Run(() => InitializeSqlServerAsync(sqlServerConnString, cts.Token));

        await Task.WhenAll(pgTask, sqlTask);

        Console.WriteLine("Database Initializer completed successfully.");
    }

    private static async Task InitializePostgresAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString)) return;

        Console.WriteLine("Waiting for Postgres to become ready...");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                Console.WriteLine("Successfully connected to Postgres.");

                // Create and seed tables
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS editor_table (
                        id SERIAL PRIMARY KEY,
                        name VARCHAR(100) NOT NULL,
                        quantity INTEGER NOT NULL,
                        price NUMERIC(10, 2) NOT NULL,
                        created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                    );

                    -- Clear and reseed to keep it fresh
                    TRUNCATE TABLE editor_table;

                    INSERT INTO editor_table (name, quantity, price) VALUES 
                    ('Postgres Widget A', 100, 19.99),
                    ('Postgres Widget B', 250, 49.50),
                    ('Postgres Gadget X', 15, 120.00),
                    ('Postgres Gadget Y', 80, 5.99),
                    ('Postgres SuperProduct', 3, 1500.00);
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);
                Console.WriteLine("Postgres test database seeded successfully.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Postgres connection failed: {ex.Message}. Retrying in 2 seconds...");
                await Task.Delay(2000, cancellationToken);
            }
        }
    }

    private static async Task InitializeSqlServerAsync(string? connectionString, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString)) return;

        Console.WriteLine("Waiting for SQL Server to become ready...");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                Console.WriteLine("Successfully connected to SQL Server.");

                // Create and seed tables
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[editor_table]') AND type in (N'U'))
                    BEGIN
                        CREATE TABLE [dbo].[editor_table] (
                            [id] INT IDENTITY(1,1) PRIMARY KEY,
                            [name] NVARCHAR(100) NOT NULL,
                            [quantity] INT NOT NULL,
                            [price] DECIMAL(10, 2) NOT NULL,
                            [created_at] DATETIMEOFFSET DEFAULT SYSDATETIMEOFFSET()
                        );
                    END

                    -- Clear and reseed to keep it fresh
                    TRUNCATE TABLE [dbo].[editor_table];

                    INSERT INTO [dbo].[editor_table] (name, quantity, price) VALUES 
                    ('SQL Server Widget A', 100, 19.99),
                    ('SQL Server Widget B', 250, 49.50),
                    ('SQL Server Gadget X', 15, 120.00),
                    ('SQL Server Gadget Y', 80, 5.99),
                    ('SQL Server SuperProduct', 3, 1500.00);
                ";
                await command.ExecuteNonQueryAsync(cancellationToken);
                Console.WriteLine("SQL Server test database seeded successfully.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SQL Server connection failed: {ex.Message}. Retrying in 3 seconds...");
                await Task.Delay(3000, cancellationToken);
            }
        }
    }
}
