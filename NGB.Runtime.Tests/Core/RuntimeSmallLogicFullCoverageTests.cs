using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.Dimensions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs.Schema;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Internal;
using NGB.Runtime.UnitOfWork;
using NGB.Runtime.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Core;

public sealed class RuntimeSmallLogicFullCoverageTests
{
    [Fact]
    public async Task DimensionSetService_CoversSingleBatchNullEmptyDeduplicationAndWrites()
    {
        var writer = new Mock<IDimensionSetWriter>(MockBehavior.Strict);
        var service = new DimensionSetService(writer.Object);

        await ((Func<Task>)(() => service.GetOrCreateIdAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await service.GetOrCreateIdAsync(DimensionBag.Empty)).Should().Be(Guid.Empty);

        var bag = Bag();
        writer.Setup(x => x.EnsureExistsAsync(
                It.IsAny<Guid>(), bag.Items, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var id = await service.GetOrCreateIdAsync(bag);
        id.Should().NotBeEmpty();

        await ((Func<Task>)(() => service.GetOrCreateIdsAsync(null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await service.GetOrCreateIdsAsync([])).Should().BeEmpty();
        await ((Func<Task>)(() => service.GetOrCreateIdsAsync([bag, null!])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        writer.Setup(x => x.EnsureExistsBatchAsync(
                It.IsAny<IReadOnlyList<DimensionSetWrite>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ids = await service.GetOrCreateIdsAsync([DimensionBag.Empty, bag, bag]);
        ids.Should().Equal(Guid.Empty, id, id);
        writer.Verify(x => x.EnsureExistsBatchAsync(
            It.Is<IReadOnlyList<DimensionSetWrite>>(sets => sets.Count == 1 && sets[0].DimensionSetId == id),
            It.IsAny<CancellationToken>()), Times.Once);

        (await service.GetOrCreateIdsAsync([DimensionBag.Empty, DimensionBag.Empty]))
            .Should().OnlyContain(x => x == Guid.Empty);
    }

    [Fact]
    public void SchemaDiagnostics_CoversIgnoredMessagesAndEveryRenderingShape()
    {
        var empty = new SchemaDiagnosticsResult();
        empty.AddError(" ");
        empty.AddWarning("");
        empty.HasErrors.Should().BeFalse();
        empty.ToSingleLineString().Should().BeEmpty();
        empty.ToString().Should().BeEmpty();

        var errors = new SchemaDiagnosticsResult();
        errors.AddError("first");
        errors.AddError("second");
        errors.HasErrors.Should().BeTrue();
        errors.Errors.Should().Equal("first", "second");
        errors.ToSingleLineString().Should().Be("Errors: first; second");
        errors.ToString().Should().Contain("Errors:").And.Contain("- first");

        var warnings = new SchemaDiagnosticsResult();
        warnings.AddWarning("warning");
        warnings.Warnings.Should().ContainSingle("warning");
        warnings.ToSingleLineString().Should().Be("Warnings: warning");
        warnings.ToString().Should().StartWith("Warnings:");

        errors.AddWarning("warning");
        errors.ToSingleLineString().Should().Be("Errors: first; second | Warnings: warning");
        errors.ToString().Should().Contain($"second{Environment.NewLine}{Environment.NewLine}Warnings:");
    }

    [Fact]
    public void ValidationFormatter_CoversLabelsSuffixRemovalAndEveryInvalidMessageFamily()
    {
        ValidationMessageFormatter.ToLabel(" ", ColumnType.String).Should().Be(" ");
        ValidationMessageFormatter.ToLabel("___", ColumnType.String).Should().Be("___");
        ValidationMessageFormatter.ToLabel("created_at_utc", ColumnType.DateTimeUtc).Should().Be("Created At");
        ValidationMessageFormatter.ToLabel("customer_id", ColumnType.Guid).Should().Be("Customer");
        ValidationMessageFormatter.ToLabel("customer_id", ColumnType.String).Should().Be("Customer Id");
        ValidationMessageFormatter.ToLabel("id", ColumnType.Guid).Should().Be("id");
        ValidationMessageFormatter.ToLabel("utc", ColumnType.DateTimeUtc).Should().Be("utc");
        ValidationMessageFormatter.RequiredFieldMessage("Customer").Should().Be("Customer is required.");
        ValidationMessageFormatter.InvalidValueMessage("Customer", ColumnType.Guid).Should().Be("Select a valid Customer.");
        ValidationMessageFormatter.InvalidValueMessage("Start", ColumnType.Date).Should().Contain("valid date");
        ValidationMessageFormatter.InvalidValueMessage("Start", ColumnType.DateTimeUtc).Should().Contain("date and time");
        ValidationMessageFormatter.InvalidValueMessage("Count", ColumnType.Int32).Should().Contain("valid number");
        ValidationMessageFormatter.InvalidValueMessage("Count", ColumnType.Int64).Should().Contain("valid number");
        ValidationMessageFormatter.InvalidValueMessage("Amount", ColumnType.Decimal).Should().Contain("valid number");
        ValidationMessageFormatter.InvalidValueMessage("Name", ColumnType.String).Should().Contain("valid value");
    }

    [Fact]
    public void BindingHelpers_CoverGuardsReferenceDeduplicationAndRuntimeTypeMatching()
    {
        Action nullList = () => DefinitionRuntimeBindingHelpers.ToReadOnlyList<object>(null!);
        Action nullType = () => DefinitionRuntimeBindingHelpers.FindMatches<object>(null!, []);
        Action nullServices = () => DefinitionRuntimeBindingHelpers.FindMatches<object>(typeof(object), null!);
        nullList.Should().Throw<NgbArgumentRequiredException>();
        nullType.Should().Throw<NgbArgumentRequiredException>();
        nullServices.Should().Throw<NgbArgumentRequiredException>();

        var first = new Derived();
        var second = new Derived();
        DefinitionRuntimeBindingHelpers.ToReadOnlyList([first, first, second]).Should().Equal(first, second);
        DefinitionRuntimeBindingHelpers.FindMatches(typeof(Derived), new Base[] { first, first, new Base(), second })
            .Should().Equal(first, second);
        DefinitionRuntimeBindingHelpers.FindMatches(typeof(string), new Base[] { first }).Should().BeEmpty();
    }

    [Fact]
    public void GeneralJournalBusinessFieldException_CoversKnownAndFormattedLabels()
    {
        var id = Guid.NewGuid();
        var reason = new GeneralJournalEntryBusinessFieldRequiredException("post", id, "ReasonCode");
        var memo = new GeneralJournalEntryBusinessFieldRequiredException("save", id, "Memo");
        var fallback = new GeneralJournalEntryBusinessFieldRequiredException("save", id, "CostCenterId");

        reason.Message.Should().StartWith("Reason code is required");
        reason.Operation.Should().Be("post");
        reason.DocumentId.Should().Be(id);
        reason.FieldName.Should().Be("ReasonCode");
        memo.Message.Should().StartWith("Memo is required");
        fallback.Message.Should().StartWith("Cost Center is required");
    }

    [Fact]
    public async Task DefinitionsHostedServiceAndException_CoverStartStopNullEmptyAndDetailedErrors()
    {
        var validator = new Mock<IDefinitionsValidationService>(MockBehavior.Strict);
        validator.Setup(x => x.ValidateOrThrowAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hosted = new DefinitionsStartupValidatorHostedService(validator.Object);
        await hosted.StartAsync(default);
        await hosted.StopAsync(default);
        validator.VerifyAll();

        Action nullErrors = () => _ = new DefinitionsValidationException(null);
        nullErrors.Should().Throw<NgbArgumentRequiredException>();
        var empty = new DefinitionsValidationException([]);
        empty.Message.Should().StartWith("Definitions validation failed.");
        var detailed = new DefinitionsValidationException(["first", "second"]);
        detailed.Errors.Should().Equal("first", "second");
        detailed.Message.Should().Contain("2 error(s)").And.Contain("- first").And.Contain("- second");
        detailed.Context["errorsCount"].Should().Be(2);
    }

    [Fact]
    public async Task UnitOfWorkExtensions_CoverGuardsSuccessExternalModeAndAllRollbackPaths()
    {
        await ((Func<Task>)(() => UnitOfWorkTransactionExtensions.ExecuteInUowTransactionAsync(
                null!, _ => Task.CompletedTask)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        var uow = UnitOfWork();
        await ((Func<Task>)(() => uow.Object.ExecuteInUowTransactionAsync((Func<CancellationToken, Task>)null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        await ((Func<Task>)(() => uow.Object.ExecuteInUowTransactionAsync(_ => Task.CompletedTask)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        var called = false;
        await uow.Object.ExecuteInUowTransactionAsync(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        called.Should().BeTrue();

        var external = UnitOfWork();
        external.Setup(x => x.EnsureActiveTransaction());
        await external.Object.ExecuteInUowTransactionAsync(false, _ => Task.CompletedTask);

        var rollback = UnitOfWork();
        var original = new InvalidOperationException("original");
        await ((Func<Task>)(() => rollback.Object.ExecuteInUowTransactionAsync(_ => Task.FromException(original))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("original");
        rollback.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);

        var brokenRollback = UnitOfWork(rollbackThrows: true);
        await ((Func<Task>)(() => brokenRollback.Object.ExecuteInUowTransactionAsync(
                _ => Task.FromException(new InvalidOperationException("preserved")))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("preserved");
    }

    [Fact]
    public async Task GenericUnitOfWorkExtensions_CoverGuardsSuccessExternalModeAndAllRollbackPaths()
    {
        await ((Func<Task>)(() => UnitOfWorkTransactionExtensions.ExecuteInUowTransactionAsync<int>(
                null!, _ => Task.FromResult(1))))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        var uow = UnitOfWork();
        await ((Func<Task>)(() => uow.Object.ExecuteInUowTransactionAsync<int>(
                (Func<CancellationToken, Task<int>>)null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        uow.SetupGet(x => x.HasActiveTransaction).Returns(true);
        await ((Func<Task>)(() => uow.Object.ExecuteInUowTransactionAsync(_ => Task.FromResult(1))))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        (await uow.Object.ExecuteInUowTransactionAsync(_ => Task.FromResult(42))).Should().Be(42);

        var external = UnitOfWork();
        external.Setup(x => x.EnsureActiveTransaction());
        (await external.Object.ExecuteInUowTransactionAsync(false, _ => Task.FromResult(7))).Should().Be(7);

        var rollback = UnitOfWork();
        await ((Func<Task>)(() => rollback.Object.ExecuteInUowTransactionAsync<int>(
                _ => Task.FromException<int>(new InvalidOperationException("original")))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("original");

        var brokenRollback = UnitOfWork(rollbackThrows: true);
        await ((Func<Task>)(() => brokenRollback.Object.ExecuteInUowTransactionAsync<int>(
                _ => Task.FromException<int>(new InvalidOperationException("preserved")))))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("preserved");
    }

    private static DimensionBag Bag()
        => new([new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]);

    private static Mock<IUnitOfWork> UnitOfWork(bool rollbackThrows = false)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Loose);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var rollback = uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()));
        if (rollbackThrows)
            rollback.ThrowsAsync(new InvalidOperationException("rollback"));
        else
            rollback.Returns(Task.CompletedTask);
        return uow;
    }

    private class Base;
    private sealed class Derived : Base;
}
