# NeoAdapter

NeoAdapter is a job orchestration platform with an API backend and an Avalonia frontend (Desktop, Browser, Android).

## Prerequisites

- .NET 10 SDK
- Optional for local API + Hangfire + PostgreSQL:
  - PostgreSQL instance
  - Valid `Postgres` connection string in `src/NeoAdapter.Api/appsettings.Development.json`
- Optional for Android target:
  - Android SDK + emulator/device

## Run the API

From repository root:

```bash
dotnet run --project src/NeoAdapter.Api/NeoAdapter.Api.csproj
```

Default local URLs are configured in `src/NeoAdapter.Api/Properties/launchSettings.json`:

- `https://localhost:7277`
- `http://localhost:5193`

## Development Authentication

The API uses local username/password authentication with JWT bearer tokens.

On startup, if the `user_accounts` table is empty, a default development account is created:

- Username: `admin`
- Password: `Admin123!`

If at least one user already exists, no default user is created.

## Run Avalonia Desktop

From repository root:

```bash
dotnet run --project src/NeoAdapter.Frontend/NeoAdapter.Frontend.Desktop/NeoAdapter.Frontend.Desktop.csproj
```

The frontend dashboard API base URL defaults to `http://localhost:5193/`.
Override it with:

```bash
export NEOADAPTER_API_BASE_URL=http://localhost:5193/
```

## Run Avalonia Browser (WASM)

From repository root:

```bash
dotnet run --project src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj
```

Default browser host URLs are configured in `src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/Properties/launchSettings.json`:

- `http://localhost:5235`

Use `http://localhost:5235` for local development.

## Run Avalonia Android

1. Start an Android emulator (or connect a physical device).
2. From repository root run:

```bash
dotnet build src/NeoAdapter.Frontend/NeoAdapter.Frontend.Android/NeoAdapter.Frontend.Android.csproj -t:Run
```

If multiple Android devices are connected, select the target device in your IDE or pass device-specific build/run properties.

## Build everything

From repository root:

```bash
dotnet build NeoAdapter.slnx
```

## Docker Compose

Run API + Browser Frontend + PostgreSQL together:

```bash
docker compose up --build
```

Services:

- Frontend (browser host): `http://localhost:5235`
- API: `http://localhost:5193`
- PostgreSQL: `localhost:5432`

Stop and remove containers:

```bash
docker compose down
```

View logs:

```bash
docker compose logs -f api
docker compose logs -f frontend-browser
docker compose logs -f postgres
```
