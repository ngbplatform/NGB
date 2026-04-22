FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NGB.AgencyBilling.Api/NGB.AgencyBilling.Api.csproj", "NGB.AgencyBilling.Api/"]
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
COPY ["NGB.AgencyBilling/NGB.AgencyBilling.csproj", "NGB.AgencyBilling/"]
COPY ["NGB.AgencyBilling.PostgreSql/NGB.AgencyBilling.PostgreSql.csproj", "NGB.AgencyBilling.PostgreSql/"]
COPY ["NGB.PostgreSql/NGB.PostgreSql.csproj", "NGB.PostgreSql/"]
COPY ["NGB.AgencyBilling.Runtime/NGB.AgencyBilling.Runtime.csproj", "NGB.AgencyBilling.Runtime/"]
RUN dotnet restore "NGB.AgencyBilling.Api/NGB.AgencyBilling.Api.csproj"
COPY . .
WORKDIR "/src/NGB.AgencyBilling.Api"
RUN dotnet build "./NGB.AgencyBilling.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NGB.AgencyBilling.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NGB.AgencyBilling.Api.dll"]
