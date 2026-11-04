using CertificationApp.PostgreSql;
using NGB.Migrator.Core;

_ = typeof(CheckpointMigrationPack).Assembly;
return await PlatformMigratorCli.RunAsync(args);
