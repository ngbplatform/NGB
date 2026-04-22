using Dapper;
using FluentAssertions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.DatabaseGuards;

/// <summary>
/// P1: Database-level constraints for platform_users (identity projection).
/// Ensures core CHECK/UNIQUE invariants are enforced even if application services are bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseConstraints_PlatformUsers_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static async Task<Guid> InsertValidUserAsync(NpgsqlConnection conn, string? authSubject = null)
    {
        var id = Guid.CreateVersion7();

        const string sql = """
                           INSERT INTO platform_users(
                               user_id,
                               auth_subject,
                               email,
                               display_name,
                               is_active
                           )
                           VALUES (
                               @UserId,
                               @AuthSubject,
                               @Email,
                               @DisplayName,
                               TRUE
                           );
                           """;

        await conn.ExecuteAsync(sql, new
        {
            UserId = id,
            AuthSubject = authSubject ?? $"it-sub-{id:N}",
            Email = $"it-{id:N}@example.test",
            DisplayName = "IT User"
        });

        return id;
    }

    [Fact]
    public async Task AuthSubject_MustBeNonEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active)
            VALUES (@UserId, @AuthSubject, NULL, NULL, TRUE);
            """,
            new { UserId = Guid.CreateVersion7(), AuthSubject = "   " });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_users_auth_subject_nonempty");
    }

    [Fact]
    public async Task Email_WhenProvided_MustBeNonEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active)
            VALUES (@UserId, @AuthSubject, @Email, NULL, TRUE);
            """,
            new { UserId = Guid.CreateVersion7(), AuthSubject = "it-email", Email = "   " });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_users_email_nonempty");
    }

    [Fact]
    public async Task DisplayName_WhenProvided_MustBeNonEmpty_CheckIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        Func<Task> act = () => conn.ExecuteAsync(
            """
            INSERT INTO platform_users(user_id, auth_subject, email, display_name, is_active)
            VALUES (@UserId, @AuthSubject, NULL, @DisplayName, TRUE);
            """,
            new { UserId = Guid.CreateVersion7(), AuthSubject = "it-dn", DisplayName = "   " });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.Which.ConstraintName.Should().Be("ck_platform_users_display_name_nonempty");
    }

    [Fact]
    public async Task AuthSubject_IsUnique_IndexIsEnforced_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        const string subject = "it-unique-subject";
        _ = await InsertValidUserAsync(conn, subject);

        Func<Task> act = () => InsertValidUserAsync(conn, subject);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        // Unique index name (created by PlatformUsersIndexesMigration)
        ex.Which.ConstraintName.Should().Be("ux_platform_users_auth_subject");
    }

    [Fact]
    public async Task ValidRows_AreAccepted_ByDb()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var id = await InsertValidUserAsync(conn);

        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM platform_users
            WHERE user_id = @Id;
            """,
            new { Id = id });

        count.Should().Be(1);
    }
}
