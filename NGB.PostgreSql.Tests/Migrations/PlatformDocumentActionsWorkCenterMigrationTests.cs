using FluentAssertions;
using NGB.PostgreSql.Migrations.Platform;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class PlatformDocumentActionsWorkCenterMigrationTests
{
    [Fact]
    public void Migration_contains_atomic_action_outbox_and_work_center_storage_contracts()
    {
        var sql = new PlatformDocumentActionsWorkCenterMigration().Generate();

        sql.Should().Contain("ADD COLUMN IF NOT EXISTS version bigint NOT NULL DEFAULT 1");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_document_action_executions");
        sql.Should().Contain("UNIQUE (idempotency_key)");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_outbox_events");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_outbox_consumer_state");
        sql.Should().Contain("ix_platform_outbox_consumer_pending");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_tasks");
        sql.Should().Contain("ck_platform_tasks_assignment");
        sql.Should().Contain("preference_code varchar(128) NULL");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_task_recipients");
        sql.Should().Contain("ix_platform_task_recipients_user");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_notification_deliveries");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_user_notification_preferences");
        sql.Should().NotContain("pm.task.apply_receivable_payment.assigned");
        sql.Should().NotContain("DELETE FROM platform_notifications");
        sql.Should().NotContain("UPDATE platform_tasks");
    }

    [Fact]
    public void Assembly_contains_one_document_actions_work_center_versioned_migration()
    {
        var assembly = typeof(PlatformDocumentActionsWorkCenterMigration).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(static name =>
                name.Contains("document_actions_work_center", StringComparison.OrdinalIgnoreCase)
                || name.Contains("work_center_task_preference_recipients", StringComparison.OrdinalIgnoreCase)
                || name.Contains("separate_work_center_tasks_and_notifications", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var resourceName = resources.Should().ContainSingle().Which;
        resourceName.Should().EndWith(
            ".db.migrations.V2026_07_26_0100__ngb_platform_document_actions_work_center.sql");

        using var stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!);
        var sql = reader.ReadToEnd();

        sql.Should().Contain("preference_code varchar(128) NULL");
        sql.Should().Contain("CREATE TABLE IF NOT EXISTS platform_task_recipients");
        sql.Should().Contain("ix_platform_task_recipients_user");
        sql.Should().NotContain("pm.task.apply_receivable_payment.assigned");
        sql.Should().NotContain("UPDATE platform_tasks");
        sql.Should().NotContain("DELETE FROM platform_notifications");
    }
}
