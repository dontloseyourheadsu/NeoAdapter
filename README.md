# NeoAdapter

NeoAdapter is a job orchestration platform with an API backend and an Avalonia frontend (Desktop, Browser, Android).

## Run With .NET Aspire (Recommended)

Aspire now orchestrates the local development stack for:

- PostgreSQL 16
- NeoAdapter API
- Avalonia Browser frontend

From repository root:

```bash
dotnet run --project src/NeoAdapter.AppHost/NeoAdapter.AppHost.csproj
```

Open the Aspire dashboard URL printed in the terminal, then launch:

- `api`
- `frontend-browser`

The AppHost wires:

- `ConnectionStrings:Postgres` for the API from the managed PostgreSQL resource
- `NEOADAPTER_API_BASE_URL` for the browser frontend from the API HTTP endpoint

This removes the need for Docker Compose during normal local development.

## Prerequisites

- .NET 10 SDK
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

Swagger UI:

- `http://localhost:5193/swagger`

Health and ping endpoints:

- `/health`
- `/ping`
- `/api/auth/ping`
- `/api/connectors/ping`
- `/api/dashboard/ping`
- `/api/integration-jobs/ping`

## Development Authentication

The API uses local username/password authentication with JWT bearer tokens.

In Development environment, the API validates a default development admin account on startup:

- Username: `admin`
- Password: `Admin123!`

If `admin` is missing, it is created.
If `admin` exists but has a different password hash, it is reset to `Admin123!`.

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

Default browser host URLs are configured in `src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/Properties/launchSettings.json`.

When running through Aspire, the browser URL is assigned by Aspire and shown in the dashboard.

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

Aspire is the supported local orchestration path.
