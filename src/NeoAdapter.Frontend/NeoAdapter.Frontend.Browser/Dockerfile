FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Native WASM compilation in dotnet publish requires `python` in PATH.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && ln -sf /usr/bin/python3 /usr/bin/python \
    && rm -rf /var/lib/apt/lists/*

COPY . .
RUN dotnet workload restore src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj
RUN dotnet restore src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj
RUN dotnet publish src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM nginx:1.27-alpine AS final
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .

EXPOSE 80
