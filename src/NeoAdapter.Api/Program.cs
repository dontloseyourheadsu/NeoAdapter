using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NeoAdapter.Application.Database;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.DependencyInjection;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

// Add services to the container.
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ChangeThisInDevelopmentOnly_AtLeast32Characters";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "NeoAdapter";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "NeoAdapter.Client";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddDbContext<NeoAdapterDbContext>(options =>
    options.UseNpgsql(postgresConnectionString));

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(postgresConnectionString)));
builder.Services.AddHangfireServer();
builder.Services.AddDataProtection();

builder.Services.AddNeoAdapterApplicationServices();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
    var sqlSecretProtector = scope.ServiceProvider.GetRequiredService<ISqlSecretProtector>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var integrationJobScheduler = scope.ServiceProvider.GetRequiredService<IIntegrationJobScheduler>();
    await dbContext.Database.EnsureCreatedAsync();
    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS connectors (
            id uuid PRIMARY KEY,
            name character varying(120) NOT NULL UNIQUE,
            type character varying(16) NOT NULL,
            sql_server character varying(255),
            sql_port integer,
            sql_database character varying(255),
            sql_username character varying(255),
            sql_password character varying(255),
            sql_table character varying(255),
            sql_trust_server_certificate boolean NOT NULL DEFAULT false,
            csv_path character varying(1000),
            csv_delimiter character varying(4) NOT NULL DEFAULT ',',
            created_at_utc timestamp with time zone NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS integration_jobs (
            id uuid PRIMARY KEY,
            name character varying(120) NOT NULL UNIQUE,
            source_connector_id uuid NOT NULL REFERENCES connectors(id) ON DELETE RESTRICT,
            destination_connector_id uuid NOT NULL REFERENCES connectors(id) ON DELETE RESTRICT,
            is_enabled boolean NOT NULL,
            cron_expression character varying(120),
            created_at_utc timestamp with time zone NOT NULL,
            updated_at_utc timestamp with time zone NOT NULL
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS integration_job_runs (
            id uuid PRIMARY KEY,
            integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
            status character varying(32) NOT NULL,
            message character varying(1000) NOT NULL,
            started_at_utc timestamp with time zone NOT NULL,
            finished_at_utc timestamp with time zone,
            records_processed integer NOT NULL DEFAULT 0,
            hangfire_job_id character varying(64)
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS user_accounts (
            id uuid PRIMARY KEY,
            username character varying(80) NOT NULL UNIQUE,
            password_hash character varying(200) NOT NULL,
            password_salt character varying(200) NOT NULL,
            created_at_utc timestamp with time zone NOT NULL,
            last_login_at_utc timestamp with time zone
        );
    ");
    await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_integration_job_runs_job_started ON integration_job_runs (integration_job_id, started_at_utc);");
    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS integration_jobs ADD COLUMN IF NOT EXISTS cron_expression character varying(120);");
    await NeoAdapterSeedData.SeedAsync(dbContext, sqlSecretProtector, CancellationToken.None);

    var hasUsers = await dbContext.UserAccounts.AnyAsync();
    if (!hasUsers)
    {
        var (hash, salt) = passwordHasher.HashPassword("Admin123!");
        dbContext.UserAccounts.Add(new NeoAdapter.Domain.UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "admin",
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastLoginAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    await integrationJobScheduler.SyncAllAsync(CancellationToken.None);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");

app.MapControllers();

app.Run();
