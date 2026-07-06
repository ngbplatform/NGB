FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NuGet.config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Build.targets", "./"]
COPY ["artifacts/nuget/", "artifacts/nuget/"]
COPY ["NGB.CRM.Migrator/NGB.CRM.Migrator.csproj", "NGB.CRM.Migrator/"]
COPY ["NGB.CRM/NGB.CRM.csproj", "NGB.CRM/"]
COPY ["NGB.CRM.PostgreSql/NGB.CRM.PostgreSql.csproj", "NGB.CRM.PostgreSql/"]
COPY ["NGB.CRM.Runtime/NGB.CRM.Runtime.csproj", "NGB.CRM.Runtime/"]
RUN dotnet restore "NGB.CRM.Migrator/NGB.CRM.Migrator.csproj" --configfile NuGet.config
COPY . .
WORKDIR "/src/NGB.CRM.Migrator"
RUN dotnet build "./NGB.CRM.Migrator.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.CRM.Migrator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.CRM.Migrator.dll"]
