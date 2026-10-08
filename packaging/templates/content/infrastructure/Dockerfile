FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
ARG PROJECT
ARG NUGET_CONFIG=NuGet.Config
WORKDIR /source
COPY . .
RUN dotnet restore "${PROJECT}/${PROJECT}.csproj" --locked-mode --configfile "${NUGET_CONFIG}"
RUN dotnet publish "${PROJECT}/${PROJECT}.csproj" -c Release --no-restore -o /output /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
ARG PROJECT
WORKDIR /app
COPY --from=build /output .
COPY infrastructure/migrate.sh /app/migrate.sh
ENV ASPNETCORE_HTTP_PORTS=8080
ENV APP_DLL=${PROJECT}.dll
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$APP_DLL\""]
