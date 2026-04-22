using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseGuards_PlatformUsers_DbConstraints_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CheckConstraint_Forbids_WhitespaceAuthSubject()
    {
        Func<Task> act = () => InsertPlatformUserAsync(
            Fixture.ConnectionString,
            userId: Guid.CreateVersion7(),
            authSubject: "   ",
            email: null,
            displayName: null,
            isActive: true);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_platform_users_auth_subject_nonempty");
    }

    [Fact]
    public async Task CheckConstraint_Forbids_WhitespaceEmail_WhenNotNull()
    {
        Func<Task> act = () => InsertPlatformUserAsync(
            Fixture.ConnectionString,
            userId: Guid.CreateVersion7(),
            authSubject: "kc|p0-user-email",
            email: "   ",
            displayName: null,
            isActive: true);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_platform_users_email_nonempty");
    }

    [Fact]
    public async Task CheckConstraint_Forbids_WhitespaceDisplayName_WhenNotNull()
    {
        Func<Task> act = () => InsertPlatformUserAsync(
            Fixture.ConnectionString,
            userId: Guid.CreateVersion7(),
            authSubject: "kc|p0-user-name",
            email: null,
            displayName: "  ",
            isActive: true);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_platform_users_display_name_nonempty");
    }

    [Fact]
    public async Task UniqueIndex_Forbids_DuplicateAuthSubject()
    {
        var authSubject = "kc|p0-dup";

        await InsertPlatformUserAsync(
            Fixture.ConnectionString,
            userId: Guid.CreateVersion7(),
            authSubject: authSubject,
            email: "p0.dup@example.com",
            displayName: "P0 Dup",
            isActive: true);

        Func<Task> act = () => InsertPlatformUserAsync(
            Fixture.ConnectionString,
            userId: Guid.CreateVersion7(),
            authSubject: authSubject,
            email: "p0.dup2@example.com",
            displayName: "P0 Dup 2",
            isActive: true);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_platform_users_auth_subject");
    }

    private static async Task InsertPlatformUserAsync(
        string cs,
        Guid userId,
        string authSubject,
        string? email,
        string? displayName,
        bool isActive)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active)
            VALUES (@user_id, @auth_subject, @email, @display_name, @is_active);
            """, conn);

        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("auth_subject", authSubject);
        cmd.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("display_name", (object?)displayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("is_active", isActive);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
