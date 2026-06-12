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
using Scalar.AspNetCore;
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
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var configuredCorsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var corsOrigins = (configuredCorsOrigins is { Length: > 0 })
    ? configuredCorsOrigins
    : ["http://localhost:5193", "http://127.0.0.1:5193", "https://localhost:7277"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin =>
            {
                if (applicableConfiguredOrigin(origin, corsOrigins))
                {
                    return true;
                }

                if (!builder.Environment.IsDevelopment())
                {
                    return false;
                }

                return Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                    && (string.Equals(originUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                        || originUri.Host == "127.0.0.1");
            });
    });
});

static bool applicableConfiguredOrigin(string origin, string[] allowedOrigins)
{
    return allowedOrigins.Any(allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapGet("/swagger", () => Results.Redirect("/scalar", permanent: false));
    app.MapGet("/swagger/index.html", () => Results.Redirect("/scalar", permanent: false));
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NeoAdapterDbContext>();
    var sqlSecretProtector = scope.ServiceProvider.GetRequiredService<ISqlSecretProtector>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var integrationJobScheduler = scope.ServiceProvider.GetRequiredService<IIntegrationJobScheduler>();
    
    Console.WriteLine("Initializing database schema and seed data...");
    try 
    {
        await dbContext.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS organizations (id uuid PRIMARY KEY, name character varying(120) NOT NULL UNIQUE, created_at_utc timestamp with time zone NOT NULL);");
        
        await dbContext.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS groups (
            id uuid PRIMARY KEY, 
            name character varying(120) NOT NULL, 
            organization_id uuid NOT NULL REFERENCES organizations(id) ON DELETE CASCADE, 
            creator_user_id uuid NOT NULL, 
            created_at_utc timestamp with time zone NOT NULL
        );");
        
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS connectors (
                id uuid PRIMARY KEY,
                name character varying(120) NOT NULL UNIQUE,
                type character varying(16) NOT NULL,
                sql_host character varying(255),
                sql_port integer,
                sql_database character varying(255),
                sql_username character varying(255),
                sql_password character varying(255),
                sql_config_json jsonb,
                sql_trust_server_certificate boolean NOT NULL DEFAULT false,
                csv_path character varying(1000),
                csv_delimiter character varying(4) NOT NULL DEFAULT ',',
                excel_path character varying(1000),
                excel_sheet_name character varying(255),
                created_at_utc timestamp with time zone NOT NULL,
                updated_at_utc timestamp with time zone NOT NULL
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            DO $$ 
            BEGIN 
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='connectors' AND column_name='sql_server') THEN
                    ALTER TABLE connectors RENAME COLUMN sql_server TO sql_host;
                END IF;
            END $$;");
        
        await dbContext.Database.ExecuteSqlRawAsync("UPDATE connectors SET type = 'SqlServer' WHERE type = 'Sql';");
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sql_config_json jsonb;"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS excel_sheet_name character varying(255);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS local_path character varying(1000);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sftp_host character varying(255);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sftp_port integer;"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sftp_username character varying(255);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sftp_password character varying(255);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE connectors ADD COLUMN IF NOT EXISTS sftp_remote_path character varying(1000);"); } catch {}


        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_jobs (
                id uuid PRIMARY KEY,
                name character varying(120) NOT NULL UNIQUE,
                owner_user_id uuid,
                owner_group_id uuid REFERENCES groups(id) ON DELETE CASCADE,
                owner_organization_id uuid REFERENCES organizations(id) ON DELETE CASCADE,
                is_enabled boolean NOT NULL,
                cron_expression character varying(120),
                created_at_utc timestamp with time zone NOT NULL,
                updated_at_utc timestamp with time zone NOT NULL
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_steps (
                id uuid PRIMARY KEY,
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                order_index integer NOT NULL,
                source_connector_id uuid NOT NULL REFERENCES connectors(id) ON DELETE RESTRICT,
                destination_connector_id uuid NOT NULL REFERENCES connectors(id) ON DELETE RESTRICT
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_groups (
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                group_id uuid NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
                PRIMARY KEY (integration_job_id, group_id)
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='integration_jobs' AND column_name='owner_group_id') THEN
                    INSERT INTO integration_job_groups (integration_job_id, group_id)
                    SELECT id, owner_group_id
                    FROM integration_jobs
                    WHERE owner_group_id IS NOT NULL
                    ON CONFLICT DO NOTHING;
                END IF;
            END $$;
        ");

        
        await dbContext.Database.ExecuteSqlRawAsync(@"
            DO $$ 
            BEGIN 
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='integration_jobs' AND column_name='source_connector_id') THEN
                    ALTER TABLE integration_jobs DROP COLUMN source_connector_id;
                END IF;
                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='integration_jobs' AND column_name='destination_connector_id') THEN
                    ALTER TABLE integration_jobs DROP COLUMN destination_connector_id;
                END IF;
            END $$;");

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
            DO $$ 
            BEGIN 
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='integration_job_runs' AND column_name='started_by') THEN
                    ALTER TABLE integration_job_runs ADD COLUMN started_by character varying(100) NOT NULL DEFAULT 'System';
                END IF;
            END $$;");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_logs (
                id uuid PRIMARY KEY,
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                integration_job_run_id uuid REFERENCES integration_job_runs(id) ON DELETE CASCADE,
                timestamp_utc timestamp with time zone NOT NULL,
                log_level character varying(16) NOT NULL,
                message character varying(2000) NOT NULL,
                details text
            );
        ");
        
        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS user_accounts (
                id uuid PRIMARY KEY,
                username character varying(80) NOT NULL UNIQUE,
                password_hash character varying(200) NOT NULL,
                password_salt character varying(200) NOT NULL,
                organization_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
                group_id uuid REFERENCES groups(id) ON DELETE SET NULL,
                role character varying(20) NOT NULL DEFAULT 'User',
                role_read boolean NOT NULL DEFAULT true,
                role_edit boolean NOT NULL DEFAULT true,
                role_create boolean NOT NULL DEFAULT true,
                role_admin boolean NOT NULL DEFAULT false,
                created_at_utc timestamp with time zone NOT NULL,
                last_login_at_utc timestamp with time zone,
                google_id character varying(100),
                microsoft_id character varying(100),
                email character varying(255)
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS user_refresh_tokens (
                id uuid PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES user_accounts(id) ON DELETE CASCADE,
                token character varying(500) NOT NULL UNIQUE,
                expires_at_utc timestamp with time zone NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                is_revoked boolean NOT NULL DEFAULT false,
                is_used boolean NOT NULL DEFAULT false
            );
        ");

        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE integration_jobs ADD COLUMN IF NOT EXISTS creator_user_id uuid REFERENCES user_accounts(id) ON DELETE SET NULL;"); } catch {}

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_owners (
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                user_account_id uuid NOT NULL REFERENCES user_accounts(id) ON DELETE CASCADE,
                PRIMARY KEY (integration_job_id, user_account_id)
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            INSERT INTO integration_job_owners (integration_job_id, user_account_id)
            SELECT id, owner_user_id
            FROM integration_jobs
            WHERE owner_user_id IS NOT NULL
            ON CONFLICT DO NOTHING;
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            UPDATE integration_jobs
            SET creator_user_id = owner_user_id
            WHERE creator_user_id IS NULL AND owner_user_id IS NOT NULL;
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_guests (
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES user_accounts(id) ON DELETE CASCADE,
                can_read boolean NOT NULL DEFAULT true,
                can_edit boolean NOT NULL DEFAULT false,
                can_create_connectors boolean NOT NULL DEFAULT false,
                PRIMARY KEY (integration_job_id, user_id)
            );
        ");

        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE integration_jobs ADD COLUMN IF NOT EXISTS password_hash character varying(255);"); } catch {}
        try { await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE integration_jobs ADD COLUMN IF NOT EXISTS password_salt character varying(255);"); } catch {}

        await dbContext.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS integration_job_password_unlocks (
                integration_job_id uuid NOT NULL REFERENCES integration_jobs(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES user_accounts(id) ON DELETE CASCADE,
                unlocked_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (integration_job_id, user_id)
            );
        ");

        await dbContext.Database.ExecuteSqlRawAsync(@"
            DO $$ 
            DECLARE 
                default_org_id uuid;
            BEGIN 
                IF NOT EXISTS (SELECT 1 FROM organizations) THEN
                    default_org_id := '00000000-0000-0000-0000-000000000001';
                    INSERT INTO organizations (id, name, created_at_utc) 
                    VALUES (default_org_id, 'NeoAdapter', now());
                ELSE
                    UPDATE organizations SET name = 'NeoAdapter' WHERE name = 'Default Organization' OR name = 'NeoAdapter Default Org';
                    SELECT id INTO default_org_id FROM organizations WHERE name = 'NeoAdapter' LIMIT 1;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='organization_id') THEN
                    ALTER TABLE user_accounts ADD COLUMN organization_id uuid REFERENCES organizations(id) ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='role') THEN
                    ALTER TABLE user_accounts ADD COLUMN role character varying(20) NOT NULL DEFAULT 'User';
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='role_read') THEN
                    ALTER TABLE user_accounts ADD COLUMN role_read boolean NOT NULL DEFAULT true;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='role_edit') THEN
                    ALTER TABLE user_accounts ADD COLUMN role_edit boolean NOT NULL DEFAULT true;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='role_create') THEN
                    ALTER TABLE user_accounts ADD COLUMN role_create boolean NOT NULL DEFAULT true;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='role_admin') THEN
                    ALTER TABLE user_accounts ADD COLUMN role_admin boolean NOT NULL DEFAULT false;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='google_id') THEN
                    ALTER TABLE user_accounts ADD COLUMN google_id character varying(100);
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='microsoft_id') THEN
                    ALTER TABLE user_accounts ADD COLUMN microsoft_id character varying(100);
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='user_accounts' AND column_name='email') THEN
                    ALTER TABLE user_accounts ADD COLUMN email character varying(255);
                END IF;

                -- Update seeded admin to have role_admin = true
                UPDATE user_accounts SET role_admin = true, role = 'Admin' WHERE username = 'admin';

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='connectors' AND column_name='owner_organization_id') THEN
                    ALTER TABLE connectors ADD COLUMN owner_user_id uuid REFERENCES user_accounts(id) ON DELETE SET NULL;
                    ALTER TABLE connectors ADD COLUMN owner_group_id uuid REFERENCES groups(id) ON DELETE SET NULL;
                    ALTER TABLE connectors ADD COLUMN owner_organization_id uuid REFERENCES organizations(id) ON DELETE RESTRICT;
                END IF;

                UPDATE user_accounts SET organization_id = default_org_id WHERE organization_id IS NULL;
                ALTER TABLE user_accounts ALTER COLUMN organization_id SET NOT NULL;

                UPDATE connectors SET owner_organization_id = default_org_id WHERE owner_organization_id IS NULL;
            END $$;");

        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_integration_job_runs_job_started ON integration_job_runs (integration_job_id, started_at_utc);");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS ix_integration_job_logs_job_timestamp ON integration_job_logs (integration_job_id, timestamp_utc);");
        
        await NeoAdapterSeedData.SeedAsync(dbContext, sqlSecretProtector, passwordHasher, CancellationToken.None);
        await integrationJobScheduler.SyncAllAsync(CancellationToken.None);
        
        Console.WriteLine("Database initialization completed successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database initialization failed: {ex.Message}");
        // Don't throw, let the app start but it might be in a degraded state.
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");

app.MapGet("/ping", () => Results.Ok(new
{
    status = "ok",
    service = "neo-adapter-api",
    timestampUtc = DateTimeOffset.UtcNow
}));
app.MapHealthChecks("/health");

app.MapControllers();

app.Run();

public partial class Program { }
