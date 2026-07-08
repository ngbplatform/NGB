FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
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
COPY ["NGB.CRM.Api/NGB.CRM.Api.csproj", "NGB.CRM.Api/"]
COPY ["NGB.CRM/NGB.CRM.csproj", "NGB.CRM/"]
COPY ["NGB.CRM.PostgreSql/NGB.CRM.PostgreSql.csproj", "NGB.CRM.PostgreSql/"]
COPY ["NGB.CRM.Runtime/NGB.CRM.Runtime.csproj", "NGB.CRM.Runtime/"]
RUN dotnet restore "NGB.CRM.Api/NGB.CRM.Api.csproj" --configfile "$NUGET_CONFIG"
COPY . .
WORKDIR "/src/NGB.CRM.Api"
RUN dotnet build "./NGB.CRM.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.CRM.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.CRM.Api.dll"]
