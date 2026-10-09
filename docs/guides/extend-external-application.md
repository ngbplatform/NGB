# Extend an external application

Start from the [official starter](../architecture/external-app-upgrades.md), named
`MyApplication`. This example adds a catalog, validation, a small orchestration
service, PostgreSQL persistence and a migration pack. The catalog is used through
the HTTP API; its browser page and menu entry are separate application work.
Add these projects when your application needs these responsibilities.

## Projects and dependencies

Run from the generated `MyApplication` directory. Use the existing
`NgbPlatformVersion` throughout; do not add these projects inside the NGB repository.

```sh
dotnet new classlib -n MyApplication.Definitions -f net10.0 --no-restore
dotnet new classlib -n MyApplication.Runtime -f net10.0 --no-restore
dotnet new classlib -n MyApplication.PostgreSql -f net10.0 --no-restore
dotnet sln MyApplication.slnx add MyApplication.Definitions/MyApplication.Definitions.csproj MyApplication.Runtime/MyApplication.Runtime.csproj MyApplication.PostgreSql/MyApplication.PostgreSql.csproj
```

Remove the unused generated `Class1.cs` files. Replace the project files with the
following. `NgbPlatformVersion` comes from the generated `Directory.Build.props`.

`MyApplication.Definitions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="NGB.Platform.Definitions" Version="[$(NgbPlatformVersion)]" />
        <PackageReference Include="NGB.Platform.Metadata" Version="[$(NgbPlatformVersion)]" />
    </ItemGroup>

</Project>
```

`MyApplication.Runtime.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="../MyApplication.Definitions/MyApplication.Definitions.csproj" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="NGB.Platform.Definitions" Version="[$(NgbPlatformVersion)]" />
        <PackageReference Include="NGB.Platform.Tools" Version="[$(NgbPlatformVersion)]" />
    </ItemGroup>

</Project>
```

`MyApplication.PostgreSql.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="../MyApplication.Definitions/MyApplication.Definitions.csproj" />
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="NGB.Platform.Persistence" Version="[$(NgbPlatformVersion)]" />
        <PackageReference Include="NGB.Platform.PostgreSql" Version="[$(NgbPlatformVersion)]" />
        <PackageReference Include="Dapper" Version="2.1.89" />
    </ItemGroup>

    <ItemGroup>
        <EmbeddedResource Include="db/migrations/**/*.sql" />
    </ItemGroup>

</Project>
```

## Catalog and shared contract

`MyApplication.Definitions/CheckpointDefinitions.cs`:

```csharp
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;

namespace MyApplication.Definitions;

public sealed class CheckpointDefinitions : IDefinitionsContributor
{
    public const string CatalogCode = "certification.checkpoint";

    public void Contribute(DefinitionsBuilder builder)
    {
        builder.AddCatalog(CatalogCode, catalog => catalog.Metadata(new CatalogTypeMetadata(
            CatalogCode: CatalogCode,
            DisplayName: "Checkpoint",
            Tables:
            [
                new CatalogTableMetadata(
                    TableName: "cat_certification_checkpoint",
                    Kind: TableKind.Head,
                    Columns:
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true)
                    ],
                    Indexes: [])
            ],
            Presentation: new CatalogPresentationMetadata("cat_certification_checkpoint", "display"),
            Version: new CatalogMetadataVersion(1, "certification"))));
    }
}
```

`MyApplication.Definitions/ICheckpointStore.cs`:

```csharp
namespace MyApplication.Definitions;

public interface ICheckpointStore
{
    Task CaptureOnceAsync(Guid operationId, CancellationToken ct);
    Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct);
}
```

## Validation and orchestration

`MyApplication.Runtime/CheckpointRuntimeDefinitions.cs`:

```csharp
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;
using MyApplication.Definitions;

namespace MyApplication.Runtime;

public sealed class CheckpointRuntimeDefinitions : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        builder.ExtendCatalog(CheckpointDefinitions.CatalogCode,
            catalog => catalog.AddValidator<CheckpointValidator>());
    }
}

public sealed class CheckpointValidator : ICatalogUpsertValidator
{
    public string TypeCode => CheckpointDefinitions.CatalogCode;

    public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.Fields.GetValueOrDefault("display")?.ToString()))
            throw new InvalidCheckpointException();

        return Task.CompletedTask;
    }
}

public sealed class InvalidCheckpointException()
    : NgbValidationException("Checkpoint display is required.", "certification.checkpoint.display_required");
```

`MyApplication.Runtime/CheckpointService.cs`:

```csharp
using MyApplication.Definitions;

namespace MyApplication.Runtime;

public sealed class CheckpointService(ICheckpointStore store)
{
    public Task CaptureAsync(Guid operationId, CancellationToken ct)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("A checkpoint operation ID is required.", nameof(operationId));

        return store.CaptureOnceAsync(operationId, ct);
    }

    public Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct)
        => store.GetEffectCountAsync(operationId, ct);
}
```

## Provider and migration discovery

`MyApplication.PostgreSql/CheckpointMigrationPack.cs`:

```csharp
using NGB.Persistence.Migrations;

namespace MyApplication.PostgreSql;

public sealed class CheckpointMigrationPack : IMigrationPackContributor
{
    public IEnumerable<MigrationPack> GetPacks()
    {
        yield return new MigrationPack(
            Id: "certification",
            MigrationAssemblies: [typeof(CheckpointMigrationPack).Assembly],
            DependsOn: ["platform"]);
    }
}
```

`MyApplication.PostgreSql/CheckpointStore.cs`:

```csharp
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using MyApplication.Definitions;

namespace MyApplication.PostgreSql;

public sealed class CheckpointStore(IUnitOfWork uow) : ICheckpointStore
{
    public async Task CaptureOnceAsync(Guid operationId, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO certification_checkpoint_effects (operation_id) VALUES (@operationId) ON CONFLICT DO NOTHING",
            new { operationId },
            cancellationToken: ct));
    }

    public async Task<int> GetEffectCountAsync(Guid operationId, CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        return await uow.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM certification_checkpoint_effects WHERE operation_id = @operationId",
            new { operationId },
            cancellationToken: ct));
    }
}

public static class CheckpointPostgresRegistration
{
    public static IServiceCollection AddCheckpointPostgres(this IServiceCollection services)
    {
        services.AddScoped<ICheckpointStore, CheckpointStore>();
        services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
            provider.GetRequiredService<IUnitOfWork>(),
            CheckpointDefinitions.CatalogCode,
            "cat_certification_checkpoint",
            [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));

        return services;
    }
}
```

Create `MyApplication.PostgreSql/db/migrations/V2026_10_06_0100__certification_checkpoint.sql`:

```sql
CREATE TABLE cat_certification_checkpoint (
    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    display text NOT NULL
);

CREATE TABLE certification_checkpoint_effects (
    operation_id uuid PRIMARY KEY,
    captured_at_utc timestamptz NOT NULL DEFAULT now()
);
```

Keep applied migration names and contents immutable. The `certification` pack depends
on `platform`, so platform catalog storage exists before application DDL runs.
In this example, the application Migrator creates both tables in the database
selected by its connection string (`application` in the generated Compose stack).
The default starter does not contain this SQL; adding it is part of this example.
The starter already seeds its registered administrator; this extension does not seed
another administrator or any demo business data.

## Compose the application

In API and Background Jobs, add direct references to all three application projects
and reference the PostgreSQL project from Migrator:

```sh
dotnet add MyApplication.Api/MyApplication.Api.csproj reference MyApplication.Definitions/MyApplication.Definitions.csproj MyApplication.Runtime/MyApplication.Runtime.csproj MyApplication.PostgreSql/MyApplication.PostgreSql.csproj
dotnet add MyApplication.BackgroundJobs/MyApplication.BackgroundJobs.csproj reference MyApplication.Definitions/MyApplication.Definitions.csproj MyApplication.Runtime/MyApplication.Runtime.csproj MyApplication.PostgreSql/MyApplication.PostgreSql.csproj
dotnet add MyApplication.Migrator/MyApplication.Migrator.csproj reference MyApplication.PostgreSql/MyApplication.PostgreSql.csproj
```

Add this direct package reference to both the API and Background Jobs project files:

```xml
<ItemGroup>
    <PackageReference Include="NGB.Platform.Definitions" Version="[$(NgbPlatformVersion)]" />
</ItemGroup>
```

Put the imports at the top of each host's `Program.cs`; add the service registrations
after the generated platform registrations and before `builder.Build()`:

```csharp
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using MyApplication.Definitions;
using MyApplication.Runtime;
using MyApplication.PostgreSql;

builder.Services.AddSingleton<IDefinitionsContributor, CheckpointDefinitions>();
builder.Services.AddSingleton<IDefinitionsContributor, CheckpointRuntimeDefinitions>();
builder.Services.AddScoped<CheckpointValidator>();
builder.Services.AddScoped<ICatalogUpsertValidator>(provider =>
    provider.GetRequiredService<CheckpointValidator>());
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddCheckpointPostgres();
```

Register both the concrete validator and its contract, as shown above: definition
bindings identify the implementation type, while the validation pipeline resolves
the contract. Preserve `AddNgbRuntimeStartupValidation()` so incomplete registrations
fail startup. Runtime references only the shared store contract; PostgreSql implements
that contract without referencing Runtime.

In Migrator's `Program.cs`, put the import at the top and anchor the migration
assembly before the existing `if (args is not ["seed-administrator"])` branch:

```csharp
using MyApplication.PostgreSql;

_ = typeof(CheckpointMigrationPack).Assembly;
```

Restore with `--force-evaluate` after adding dependencies and commit all updated
lockfiles:

```sh
dotnet restore MyApplication.slnx --force-evaluate --configfile NuGet.Config
dotnet build MyApplication.slnx --no-restore -c Release
```

For local packages use `--configfile NuGet.Local.Config`. Subsequent restores should
use `--locked-mode`.

## Apply and verify

Check migration discovery before changing the database:

```sh
dotnet run --project MyApplication.Migrator -c Release --no-build -- --list-modules
dotnet run --project MyApplication.Migrator -c Release --no-build -- --dry-run --modules certification --show-scripts
```

The output must include the `certification` pack after its `platform` dependency
in the migration plan and the embedded checkpoint SQL script. These commands do
not apply migrations.

For an existing generated Compose deployment, keep PostgreSQL and Keycloak running,
take and test a backup, then apply the new migration with:

```sh
node infrastructure/ngb.mjs deploy --backup-confirmed
```

This stops API/worker/web before migration and restarts them only after it succeeds.
For a newly configured app with no existing containers, use
`node infrastructure/ngb.mjs start` instead. Check `docker compose ps -a` and
`docker compose logs migrator`; Migrator must exit with code 0 and API/jobs must
be healthy.

Use an authenticated administrator access token for the application API
(`http://localhost:5181` in the starter). Send this request from your HTTP client:

```http
POST /api/catalogs/certification.checkpoint HTTP/1.1
Host: localhost:5181
Authorization: Bearer <access-token>
Content-Type: application/json

{"fields":{"display":"First checkpoint"}}
```

Expect HTTP 200 with the created record's `id`. Read it with
`GET /api/catalogs/certification.checkpoint/{id}` and update it with `PUT` to the
same URL and the same payload shape. Creating a record with `display` set to an
empty string must return HTTP 400. A regular user needs the corresponding catalog
permissions; an active application administrator receives them automatically.

`CheckpointService` demonstrates provider-neutral orchestration. Its operation ID
identifies one logical effect, independently of the catalog record. In an existing
authorized job producer that has an `IBackgroundJobClient`, queue the service with:

```csharp
using Hangfire;
using MyApplication.Runtime;

client.Enqueue<CheckpointService>(service =>
    service.CaptureAsync(operationId, CancellationToken.None));
```

The producer must persist a nonempty `operationId` before enqueueing and reuse it
for retries. The worker resolves the service through the registrations above.
Queue the same operation twice and verify both jobs succeed in the worker dashboard;
`GetEffectCountAsync(operationId, ct)` must still return 1. This guarantees one row
for this example's database effect; it does not make arbitrary external side effects
exactly once. Add the producer to your application's authorized workflow, rather
than enqueueing jobs unconditionally during host startup.
