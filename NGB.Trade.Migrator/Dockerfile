FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
USER $APP_UID

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NGB.Trade.Migrator/NGB.Trade.Migrator.csproj", "NGB.Trade.Migrator/"]
COPY ["NGB.Migrator.Core/NGB.Migrator.Core.csproj", "NGB.Migrator.Core/"]
COPY ["NGB.PostgreSql/NGB.PostgreSql.csproj", "NGB.PostgreSql/"]
COPY ["NGB.Runtime/NGB.Runtime.csproj", "NGB.Runtime/"]
COPY ["NGB.Trade/NGB.Trade.csproj", "NGB.Trade/"]
COPY ["NGB.Trade.PostgreSql/NGB.Trade.PostgreSql.csproj", "NGB.Trade.PostgreSql/"]
COPY ["NGB.Trade.Runtime/NGB.Trade.Runtime.csproj", "NGB.Trade.Runtime/"]
COPY ["NGB.Contracts/NGB.Contracts.csproj", "NGB.Contracts/"]
COPY ["NGB.Tools/NGB.Tools.csproj", "NGB.Tools/"]
COPY ["NGB.Application.Abstractions/NGB.Application.Abstractions.csproj", "NGB.Application.Abstractions/"]
COPY ["NGB.Core/NGB.Core.csproj", "NGB.Core/"]
COPY ["NGB.Accounting/NGB.Accounting.csproj", "NGB.Accounting/"]
COPY ["NGB.Definitions/NGB.Definitions.csproj", "NGB.Definitions/"]
COPY ["NGB.Metadata/NGB.Metadata.csproj", "NGB.Metadata/"]
COPY ["NGB.OperationalRegisters/NGB.OperationalRegisters.csproj", "NGB.OperationalRegisters/"]
COPY ["NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj", "NGB.ReferenceRegisters/"]
COPY ["NGB.Persistence/NGB.Persistence.csproj", "NGB.Persistence/"]
RUN dotnet restore "NGB.Trade.Migrator/NGB.Trade.Migrator.csproj"
COPY . .
WORKDIR "/src/NGB.Trade.Migrator"
RUN dotnet build "./NGB.Trade.Migrator.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.Trade.Migrator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.Trade.Migrator.dll"]
