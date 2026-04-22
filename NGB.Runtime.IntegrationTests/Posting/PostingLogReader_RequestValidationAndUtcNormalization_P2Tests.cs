using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P2: Contract tests for PostingLogReader request validation and time-bound normalization.
/// These are pure reader tests and should stay stable, because Admin/UX relies on them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLogReader_RequestValidationAndUtcNormalization_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetPageAsync_WhenToUtcLessThanFromUtc_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        var from = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var act = () => reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = from,
            ToUtc = to,
            PageSize = 10,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("ToUtc");
        ex.Which.Reason.Should().Contain("To must be on or after From.");
    }

    [Fact]
    public async Task GetPageAsync_WhenBoundsAreUnspecifiedDateTime_TreatsThemAsUtc_AndReturnsRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Seed a deterministic row.
        var docId = Guid.CreateVersion7();
        var startedAtUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddMinutes(1);

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            const string insert = """
                            INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc)
                            VALUES (@d, @op, @s, @c);
                            """;

            await using var cmd = new NpgsqlCommand(insert, conn);
            cmd.Parameters.AddWithValue("d", docId);
            cmd.Parameters.AddWithValue("op", (short)PostingOperation.Post);
            cmd.Parameters.AddWithValue("s", startedAtUtc);
            cmd.Parameters.AddWithValue("c", completedAtUtc);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        // These bounds intentionally have Kind=Unspecified (common when deserializing JSON without DateTimeZone).
        var fromUnspecified = DateTime.SpecifyKind(startedAtUtc.AddHours(-1), DateTimeKind.Unspecified);
        var toUnspecified = DateTime.SpecifyKind(startedAtUtc.AddHours(1), DateTimeKind.Unspecified);

        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = fromUnspecified,
            ToUtc = toUnspecified,
            PageSize = 100,
            StaleAfter = TimeSpan.FromDays(3650)
        }, CancellationToken.None);

        page.Records.Should().ContainSingle(r => r.DocumentId == docId && r.Operation == PostingOperation.Post);
        page.Records.Single(r => r.DocumentId == docId).StartedAtUtc.Should().Be(startedAtUtc);
    }
}
