FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NGB.Trade.Api/NGB.Trade.Api.csproj", "NGB.Trade.Api/"]
COPY ["NGB.Api/NGB.Api.csproj", "NGB.Api/"]
COPY ["NGB.Contracts/NGB.Contracts.csproj", "NGB.Contracts/"]
COPY ["NGB.Tools/NGB.Tools.csproj", "NGB.Tools/"]
COPY ["NGB.Application.Abstractions/NGB.Application.Abstractions.csproj", "NGB.Application.Abstractions/"]
COPY ["NGB.Core/NGB.Core.csproj", "NGB.Core/"]
COPY ["NGB.Runtime/NGB.Runtime.csproj", "NGB.Runtime/"]
COPY ["NGB.Accounting/NGB.Accounting.csproj", "NGB.Accounting/"]
COPY ["NGB.Definitions/NGB.Definitions.csproj", "NGB.Definitions/"]
COPY ["NGB.Metadata/NGB.Metadata.csproj", "NGB.Metadata/"]
COPY ["NGB.OperationalRegisters/NGB.OperationalRegisters.csproj", "NGB.OperationalRegisters/"]
COPY ["NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj", "NGB.ReferenceRegisters/"]
COPY ["NGB.Persistence/NGB.Persistence.csproj", "NGB.Persistence/"]
COPY ["NGB.Trade/NGB.Trade.csproj", "NGB.Trade/"]
COPY ["NGB.Trade.PostgreSql/NGB.Trade.PostgreSql.csproj", "NGB.Trade.PostgreSql/"]
COPY ["NGB.PostgreSql/NGB.PostgreSql.csproj", "NGB.PostgreSql/"]
COPY ["NGB.Trade.Runtime/NGB.Trade.Runtime.csproj", "NGB.Trade.Runtime/"]
RUN dotnet restore "NGB.Trade.Api/NGB.Trade.Api.csproj"
COPY . .
WORKDIR "/src/NGB.Trade.Api"
RUN dotnet build "./NGB.Trade.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.Trade.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.Trade.Api.dll"]
