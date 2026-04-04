FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet workload restore src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj
RUN dotnet restore src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj
RUN dotnet publish src/NeoAdapter.Frontend/NeoAdapter.Frontend.Browser/NeoAdapter.Frontend.Browser.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NeoAdapter.Frontend.Browser.dll"]
