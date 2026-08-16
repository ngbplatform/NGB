using System.Data;
using FluentAssertions;
using NGB.Accounting.PostingState;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.PostingState;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Idempotency;

public sealed class StateRepositoriesFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Document_operation_state_covers_messages_and_both_clear_modes()
    {
        var documentId = Guid.NewGuid();
        var missing = new PostgresDocumentOperationStateRepository(MissingUnitOfWork());
        Func<Task> begin = () => missing.TryBeginAsync(documentId, PostingOperation.Post, Now, default);
        await begin.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Document operation state row not found*");

        var many = new PostgresDocumentOperationStateRepository(ManyCompletedRowsUnitOfWork());
        Func<Task> complete = () => many.MarkCompletedAsync(documentId, PostingOperation.Unpost, Now, default);
        await complete.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("Document operation state update affected more than one row.");

        var clearConnection = new RecordingDbConnection();
        var clear = new PostgresDocumentOperationStateRepository(
            new RecordingUnitOfWork(clearConnection, hasActiveTransaction: true));
        await clear.ClearCompletedStateAsync(documentId, PostingOperation.Post, default);
        await clear.ClearInProgressStateAsync(documentId, PostingOperation.Post, default);
        clearConnection.Commands.Should().HaveCount(2);
        clearConnection.Commands[0].CommandText.Should().Contain("completed_at_utc IS NOT NULL");
        clearConnection.Commands[1].CommandText.Should().Contain("completed_at_utc IS NULL");
    }

    [Fact]
    public async Task Posting_state_covers_messages_and_completed_clear()
    {
        var documentId = Guid.NewGuid();
        var missing = new PostgresPostingStateRepository(MissingUnitOfWork());
        Func<Task> begin = () => missing.TryBeginAsync(documentId, PostingOperation.Repost, Now, default);
        await begin.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Posting state row not found*");

        var many = new PostgresPostingStateRepository(ManyCompletedRowsUnitOfWork());
        Func<Task> complete = () => many.MarkCompletedAsync(documentId, PostingOperation.Post, Now, default);
        await complete.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("Posting state update affected more than one row.");

        var connection = new RecordingDbConnection();
        var clear = new PostgresPostingStateRepository(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        await clear.ClearCompletedStateAsync(documentId, PostingOperation.Post, default);
        connection.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Operational_write_state_covers_messages_register_lookup_and_clear()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var missing = new PostgresOperationalRegisterWriteStateRepository(MissingUnitOfWork());
        Func<Task> begin = () => missing.TryBeginAsync(
            registerId, documentId, (NGB.OperationalRegisters.Contracts.OperationalRegisterWriteOperation)0, Now, default);
        await begin.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Operational register write state row not found*");

        var many = new PostgresOperationalRegisterWriteStateRepository(ManyCompletedRowsUnitOfWork());
        Func<Task> complete = () => many.MarkCompletedAsync(
            registerId, documentId, (NGB.OperationalRegisters.Contracts.OperationalRegisterWriteOperation)0, Now, default);
        await complete.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Failed to mark operational register write state completed*");

        var ids = GuidRows(registerId);
        var connection = new RecordingDbConnection(_ => ids.CreateDataReader());
        var sut = new PostgresOperationalRegisterWriteStateRepository(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        (await sut.GetRegisterIdsByDocumentAsync(documentId, default)).Should().Equal(registerId);
        await sut.ClearCompletedStateByDocumentAsync(
            documentId, NGB.OperationalRegisters.Contracts.OperationalRegisterWriteOperation.Post, default);
        connection.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reference_write_state_covers_messages_register_lookup_and_clear()
    {
        var registerId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var missing = new PostgresReferenceRegisterWriteStateRepository(MissingUnitOfWork());
        Func<Task> begin = () => missing.TryBeginAsync(
            registerId, documentId, (ReferenceRegisterWriteOperation)0, Now, default);
        await begin.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Reference register write state row not found*");

        var many = new PostgresReferenceRegisterWriteStateRepository(ManyCompletedRowsUnitOfWork());
        Func<Task> complete = () => many.MarkCompletedAsync(
            registerId, documentId, (ReferenceRegisterWriteOperation)0, Now, default);
        await complete.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Failed to mark reference register write state completed*");

        var ids = GuidRows(registerId);
        var connection = new RecordingDbConnection(_ => ids.CreateDataReader());
        var sut = new PostgresReferenceRegisterWriteStateRepository(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        (await sut.GetRegisterIdsByDocumentAsync(documentId, default)).Should().Equal(registerId);
        await sut.ClearCompletedStateByDocumentAsync(documentId, ReferenceRegisterWriteOperation.Post, default);
        connection.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task Independent_reference_write_state_covers_both_diagnostic_messages()
    {
        var registerId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var missing = new PostgresReferenceRegisterIndependentWriteStateRepository(MissingUnitOfWork());
        Func<Task> begin = () => missing.TryBeginAsync(
            registerId, commandId, (ReferenceRegisterIndependentWriteOperation)0, Now, default);
        await begin.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Reference register independent write state row not found*");

        var many = new PostgresReferenceRegisterIndependentWriteStateRepository(ManyCompletedRowsUnitOfWork());
        Func<Task> complete = () => many.MarkCompletedAsync(
            registerId, commandId, (ReferenceRegisterIndependentWriteOperation)0, Now, default);
        await complete.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Failed to mark reference register independent write state completed*");
    }

    private static RecordingUnitOfWork MissingUnitOfWork()
    {
        var empty = new DataTable();
        empty.Columns.Add("AttemptId", typeof(Guid));
        empty.Columns.Add("StartedAtUtc", typeof(DateTime));
        empty.Columns.Add("CompletedAtUtc", typeof(DateTime));
        return new RecordingUnitOfWork(
            new RecordingDbConnection(_ => empty.CreateDataReader(), nonQuery: _ => 0),
            hasActiveTransaction: true);
    }

    private static RecordingUnitOfWork ManyCompletedRowsUnitOfWork()
    {
        var table = new DataTable();
        table.Columns.Add("AttemptId", typeof(Guid));
        table.Columns.Add("CompletedAtUtc", typeof(DateTime));
        table.Rows.Add(Guid.NewGuid(), Now);
        table.Rows.Add(Guid.NewGuid(), Now);
        return new RecordingUnitOfWork(
            new RecordingDbConnection(_ => table.CreateDataReader()),
            hasActiveTransaction: true);
    }

    private static DataTable GuidRows(Guid value)
    {
        var table = new DataTable();
        table.Columns.Add("register_id", typeof(Guid));
        table.Rows.Add(value);
        return table;
    }
}
