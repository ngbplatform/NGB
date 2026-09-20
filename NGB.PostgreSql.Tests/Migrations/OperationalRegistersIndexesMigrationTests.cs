using FluentAssertions;
using NGB.PostgreSql.Migrations.Platform;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class OperationalRegistersIndexesMigrationTests
{
    [Fact]
    public void Generate_CreatesPartialFinalizationQueueIndexes()
    {
        var sql = new PlatformReadPathIndexesMigration().Generate();

        sql.Should()
            .Contain("ix_opreg_finalizations_dirty_queue")
            .And.Contain("ON public.operational_register_finalizations(dirty_since_utc, register_id, period)")
            .And.Contain("WHERE status = 2")
            .And.Contain("ix_opreg_finalizations_blocked_queue")
            .And.Contain("ON public.operational_register_finalizations(blocked_since_utc, register_id, period)")
            .And.Contain("WHERE status = 3");
    }

    [Fact]
    public void EvolveForwardMigration_CreatesTheSameFinalizationQueueIndexes()
    {
        const string suffix = ".db.migrations.V2026_08_26_0100__ngb_platform_read_path_indexes.sql";
        var assembly = typeof(PlatformReadPathIndexesMigration).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var sql = reader.ReadToEnd();

        sql.Should()
            .Contain("ix_opreg_finalizations_dirty_queue")
            .And.Contain("ON public.operational_register_finalizations(dirty_since_utc, register_id, period)")
            .And.Contain("WHERE status = 2")
            .And.Contain("ix_opreg_finalizations_blocked_queue")
            .And.Contain("ON public.operational_register_finalizations(blocked_since_utc, register_id, period)")
            .And.Contain("WHERE status = 3");
    }
}
