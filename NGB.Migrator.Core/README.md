# NGB.Migrator.Core

`NGB.Migrator.Core` is the **shared schema migrator CLI engine** for NGB.

It is designed to be referenced by **application-specific migrator hosts**
(e.g. `NGB.Demo.Trade.Migrator`) and executed as a **deployment step**
(CI/CD pipeline, Kubernetes Job).

> This library is **not** intended to run implicitly from your Web/API startup in production.

---

## Why a library?

NGB follows a strict operational rule:

- **Each deployable application/vertical has its own migrator executable**
  (because each app has a different module set / migration packs).

`NGB.Migrator.Core` contains the reusable CLI + orchestration logic.
Your vertical migrator project is a thin host that references the required module assemblies.

---

## What it does

A migrator run is:

1. **Migrate**: apply Evolve migrations (versioned + repeatable) for selected packs.
2. **Repair (optional)**: restore critical DDL invariants (FKs/triggers/indexes/guards) if they were removed by drift.
3. **Validate**: run platform/core schema validations and fail fast on missing/incorrect objects.

The migrator uses advisory locks to avoid concurrent schema writers.

---

## Creating an application-specific migrator host

Create a small console project, reference:

- `NGB.Migrator.Core`
- platform + vertical PostgreSql modules that contain embedded migrations
  (e.g. `NGB.PostgreSql`, `NGB.Demo.Trade.PostgreSql`)

### `Program.cs`

```csharp
using NGB.Migrator.Core;

internal static class Program
{
    public static Task<int> Main(string[] args) => PlatformMigratorCli.RunAsync(args);
}
```

### Important: reference the module assemblies

Pack discovery scans the entry assembly and its referenced assemblies.
If your host does **not** reference a module assembly, its embedded SQL scripts will not be discovered.

---

## Evolve conventions

Evolve tracks applied migrations in:

- schema: `public`
- table: `migration_changelog`

Scripts are embedded resources under:

- `<project>/db/migrations/*.sql`

Naming:

- **Versioned**: `VYYYY_MM_DD_NNNN__description.sql`
  - applied once, tracked by version/checksum
  - never edit after applying; create a new versioned migration instead

- **Repeatable**: `R__description.sql`
  - re-applied when checksum changes
  - best for FUNCTION/VIEW/installers where you want the “current definition”

---

## Repair vs Migrate (critical difference)

- **Migrate (Evolve)** is *history-based*. A versioned migration is not re-run once recorded.
  If someone drops an FK/trigger afterwards, Evolve will **not** recreate it by re-running old versioned scripts.

- **Repair** is *idempotent self-healing*. It restores critical invariants that validators check.
  Use it:
  - in dev/CI
  - for drift-repair tests
  - as a controlled maintenance operation

---

## CLI quick start (from your host project)

### List discovered packs (modules)

```bash
dotnet run --project <YourApp>.Migrator -- --list-modules
```

### Migrate platform + demo.trade

```bash
dotnet run --project <YourApp>.Migrator -- \
  --connection "Host=localhost;Database=ngb;Username=postgres;Password=postgres" \
  --modules platform,demo.trade
```

### Migrate + Repair

```bash
dotnet run --project <YourApp>.Migrator -- \
  --connection "..." \
  --modules platform,demo.trade \
  --repair
```

### Plan-only / dry-run

```bash
dotnet run --project <YourApp>.Migrator -- --dry-run --info --modules platform,demo.trade
```

---

## Argument reference

Core:

- `--connection "<connStr>"`
  - required unless `--dry-run` or `--list-modules`
  - env alternative: `NGB_CONNECTION_STRING`

- `--modules "a,b,c"` (CSV)
- `--module <id>` (repeatable)

Options:

- `--repair` : run drift-repair after migrate
- `--dry-run` / `--plan-only` : do not touch DB
- `--info` : print resolved plan
- `--show-scripts` : print embedded resource names (use with `--info`)

Locking / timeouts:

- `--schema-lock-mode wait|try|skip`
- `--schema-lock-wait-seconds <N>`
- `--lock-timeout-seconds <N>`
- `--statement-timeout-seconds <N>`

---

## Exit codes (stable)

- `0` success (including “skip due to lock” in `skip` mode)
- `1` migration failed (SQL/runtime error)
- `2` invalid arguments / configuration
- `3` schema lock not acquired (`try` or `wait` timeout)

---

## Adding a new module (migration pack)

1. Put SQL scripts under `<Module>.PostgreSql/db/migrations/`.
2. Ensure the module `.csproj` embeds them as resources.
3. Implement `IMigrationPackContributor` in that module:
   - stable `Id`
   - `DependsOn` (usually `platform`)
   - `MigrationAssemblies` includes the module assembly
4. Validate discovery:

```bash
dotnet run --project <YourApp>.Migrator -- --list-modules
dotnet run --project <YourApp>.Migrator -- --dry-run --info --modules <pack-id>
```
