FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG NUGET_CONFIG=NuGet.config
WORKDIR /src
COPY ["NuGet.config", "./"]
COPY ["NuGet.Registry.Config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Build.targets", "./"]
COPY ["artifacts/nuget/", "artifacts/nuget/"]
COPY ["NGB.CRM.Watchdog/NGB.CRM.Watchdog.csproj", "NGB.CRM.Watchdog/"]
RUN dotnet restore "NGB.CRM.Watchdog/NGB.CRM.Watchdog.csproj" --configfile "$NUGET_CONFIG"
COPY . .
WORKDIR "/src/NGB.CRM.Watchdog"
RUN dotnet build "./NGB.CRM.Watchdog.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.CRM.Watchdog.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.CRM.Watchdog.dll"]
