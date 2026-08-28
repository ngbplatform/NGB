using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Documents;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using ContractDocumentStatus = NGB.Contracts.Metadata.DocumentStatus;

namespace NGB.CRM.Runtime.Tests.Setup;

public sealed class CrmDemoSeedServiceFullCoverageTests
{
    [Fact]
    public void Constructor_RejectsNullOptions()
    {
        var create = () => _ = CreateServiceWithOptions(null!);

        create.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("options");
    }

    [Theory]
    [InlineData(0, 30, "GeneratedAccountCount")]
    [InlineData(10, 0, "GeneratedOpportunityCycleCount")]
    [InlineData(1001, 30, "GeneratedAccountCount")]
    [InlineData(10, 10001, "GeneratedOpportunityCycleCount")]
    public void Constructor_RejectsOutOfRangeProfileSizes(
        int accountCount,
        int opportunityCycleCount,
        string expectedParameter)
    {
        var options = new CrmDemoSeedOptions
        {
            GeneratedAccountCount = accountCount,
            GeneratedOpportunityCycleCount = opportunityCycleCount
        };

        var create = () => _ = CreateServiceWithOptions(options);

        create.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(expectedParameter);
    }

    [Fact]
    public async Task EnsureDemoAsync_EmptySystemCreatesTheCompleteDeterministicDemoDataset()
    {
        var state = new SeedState { GeneratedLeadCount = 0, OperationalTotal = 0 };

        var result = await CreateService(state).EnsureDemoAsync();

        result.AsOfUtc.Should().Be(new DateOnly(2026, 8, 15));
        result.AccountsEnsured.Should().Be(83);
        result.ContactsEnsured.Should().Be(83);
        result.ProductsEnsured.Should().Be(2);
        result.StagesEnsured.Should().Be(6);
        result.DocumentsCreated.Should().Be(3134);
        result.SeededOperationalData.Should().BeTrue();
        state.SetupCalls.Should().Be(1);
        state.CatalogCreates.Should().HaveCount(166);
        state.DocumentCreates.Should().HaveCount(3134);
        state.DocumentUpdates.Should().HaveCount(3134);
        state.Posts.Should().HaveCount(3134);
        state.DocumentUpdates.Should().Contain(update =>
            update.Type == CrmCodes.Quote && update.Payload.Parts!["lines"].Rows.Count == 2);
        state.DocumentUpdates.Select(update => Display(update.Payload)).Should().OnlyContain(display => display != null);
    }

    [Fact]
    public async Task EnsureDemoAsync_ExistingOperationalDataWithCompleteGeneration_IsIdempotentAndCoversCatalogMatching()
    {
        var state = new SeedState { GeneratedLeadCount = 520, OperationalTotal = 1 };
        state.CatalogEnsurePages.Enqueue([Catalog("Different", Payload(("account_number", "CRM-A100")))]);
        state.CatalogEnsurePages.Enqueue([new CatalogItemDto(Guid.CreateVersion7(), "Different", new RecordPayload(), false, false)]);
        state.CatalogEnsurePages.Enqueue([Catalog("Different", Payload(("other", "value")))]);
        state.CatalogEnsurePages.Enqueue([Catalog("Different", Payload(("account_number", "WRONG")))]);
        state.CatalogEnsurePages.Enqueue([Catalog("Priya Raman", Payload(("email", "different@example.test")))]);
        state.CatalogEnsurePages.Enqueue([]);

        var result = await CreateService(state).EnsureDemoAsync();

        result.AccountsEnsured.Should().Be(3);
        result.ContactsEnsured.Should().Be(3);
        result.DocumentsCreated.Should().Be(0);
        result.SeededOperationalData.Should().BeFalse();
        state.CatalogUpdates.Should().HaveCount(2);
        state.CatalogCreates.Should().HaveCount(4);
        state.DocumentCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDemoAsync_ExistingOperationalDataCompletesOnlyMissingGeneratedCycles()
    {
        var state = new SeedState { GeneratedLeadCount = 519, OperationalTotal = 1 };

        var result = await CreateService(state).EnsureDemoAsync();

        result.AccountsEnsured.Should().Be(83);
        result.ContactsEnsured.Should().Be(83);
        result.DocumentsCreated.Should().Be(6);
        result.SeededOperationalData.Should().BeTrue();
        state.DocumentCreates.Should().HaveCount(6);
    }

    [Fact]
    public async Task EnsureDemoAsync_BackfillsPostedDocumentsAcrossFullAndPartialPages()
    {
        var state = new SeedState { GeneratedLeadCount = 520, OperationalTotal = 1 };
        var noHandlerId = Guid.CreateVersion7();
        var leadItems = Enumerable.Range(0, 200)
            .Select(index => Document(
                ContractDocumentStatus.Posted,
                index == 1 ? noHandlerId : Guid.CreateVersion7()))
            .ToArray();
        state.SetBackfillPage(CrmCodes.LeadIntake, 0, leadItems);
        state.SetBackfillPage(CrmCodes.LeadIntake, 200, []);
        state.SetBackfillPage(CrmCodes.Quote, 0, [Document(ContractDocumentStatus.Posted)]);
        state.ResolveAction = document => document.Id == noHandlerId
            ? null
            : (builder, _, _) =>
            {
                builder.Add("crm.backfill.a", Record(recorderId: null));
                builder.Add("CRM.BACKFILL.A", Record(recorderId: Guid.Empty));
                builder.Add("crm.backfill.b", Record(recorderId: document.Id));
                return Task.CompletedTask;
            };
        state.ApplyResult = records => records.Count == 2
            ? ReferenceRegisterWriteResult.Executed
            : ReferenceRegisterWriteResult.AlreadyCompleted;

        var result = await CreateService(state).EnsureDemoAsync();

        result.DocumentsCreated.Should().Be(0);
        result.SeededOperationalData.Should().BeTrue();
        state.Applies.Should().NotBeEmpty();
        state.Applies.Should().Contain(apply => apply.Records.Count == 2);
        state.Applies.Should().Contain(apply => apply.Records.Count == 1);
        state.UnitOfWork.BeginCount.Should().Be(9);
        state.UnitOfWork.CommitCount.Should().Be(state.UnitOfWork.BeginCount);
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("null-record")]
    [InlineData("wrong-recorder")]
    public async Task EnsureDemoAsync_BackfillRejectsInvalidBuilderInput(string scenario)
    {
        var state = new SeedState { GeneratedLeadCount = 520, OperationalTotal = 1 };
        state.SetBackfillPage(CrmCodes.LeadIntake, 0, [Document(ContractDocumentStatus.Posted)]);
        state.ResolveAction = document => (builder, _, _) =>
        {
            switch (scenario)
            {
                case "blank": builder.Add(" ", Record(null)); break;
                case "null-record": builder.Add("crm.invalid", null!); break;
                default: builder.Add("crm.invalid", Record(Guid.CreateVersion7())); break;
            }

            return Task.CompletedTask;
        };
        var act = () => CreateService(state).EnsureDemoAsync();

        if (scenario == "wrong-recorder")
            await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        else
            await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        state.UnitOfWork.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task EnsureDemoAsync_RejectsDuplicateSeedCatalogMatch()
    {
        var item = Catalog("Acme Distribution", Payload(("account_number", "CRM-A100")));
        var state = new SeedState { GeneratedLeadCount = 520, OperationalTotal = 1 };
        state.CatalogEnsurePages.Enqueue([item, item with { Id = Guid.CreateVersion7() }]);
        var act = () => CreateService(state).EnsureDemoAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*Multiple*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDemoAsync_RejectsMissingOrDuplicateSetupCatalogDefaults(bool duplicate)
    {
        var state = new SeedState();
        state.LookupOverrides[$"{CrmCodes.OpportunityStage}|Qualification"] = duplicate
            ? [Catalog("Qualification", new RecordPayload()), Catalog("Qualification", new RecordPayload())]
            : [];
        var act = () => CreateService(state).EnsureDemoAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        state.CatalogCreates.Should().BeEmpty();
    }

    private static CrmDemoSeedService CreateService(SeedState state)
    {
        var setup = new Mock<ICrmSetupService>(MockBehavior.Strict);
        setup.Setup(x => x.EnsureDefaultsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => state.SetupCalls++)
            .ReturnsAsync(new NGB.CRM.Contracts.CrmSetupResult(6, 2));

        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                IReadOnlyList<CatalogItemDto> items;
                if (request.Search != null)
                {
                    var key = $"{type}|{request.Search}";
                    items = state.LookupOverrides.TryGetValue(key, out var configured)
                        ? configured
                        : [state.DefaultCatalogs[key]];
                }
                else
                {
                    items = state.CatalogEnsurePages.TryDequeue(out var configured) ? configured : [];
                }

                return new PageResponseDto<CatalogItemDto>(items, request.Offset, request.Limit, items.Count);
            });
        catalogs.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, RecordPayload, CancellationToken>((type, payload, _) => state.CatalogCreates.Add((type, payload)))
            .ReturnsAsync((string _, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(Guid.CreateVersion7(), Display(payload), payload, false, false));
        catalogs.Setup(x => x.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, RecordPayload, CancellationToken>(
                (type, id, payload, _) => state.CatalogUpdates.Add((type, id, payload)))
            .ReturnsAsync((string _, Guid id, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(id, Display(payload), payload, false, false));

        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        documents.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                if (request.Limit == 1)
                    return new PageResponseDto<DocumentDto>([], request.Offset, request.Limit, state.OperationalTotal);

                return new PageResponseDto<DocumentDto>([], request.Offset, request.Limit, 0);
            });
        documents.Setup(x => x.CreateDraftAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, RecordPayload payload, CancellationToken _) =>
            {
                var id = Guid.CreateVersion7();
                var number = state.DocumentCreates.Count % 2 == 0 ? $"CRM-{state.DocumentCreates.Count + 1:0000}" : null;
                state.DocumentCreates.Add((type, id, payload));
                return new DocumentDto(id, null, payload, ContractDocumentStatus.Draft, false, number);
            });
        documents.Setup(x => x.UpdateDraftAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, RecordPayload, CancellationToken>(
                (type, id, payload, _) => state.DocumentUpdates.Add((type, id, payload)))
            .ReturnsAsync((string _, Guid id, RecordPayload payload, CancellationToken _) =>
                new DocumentDto(id, Display(payload), payload, ContractDocumentStatus.Draft, false));

        var lifecycle = new Mock<IDocumentSystemLifecycleService>(MockBehavior.Strict);
        lifecycle.Setup(x => x.PostAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((type, id, _) => state.Posts.Add((type, id)))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                new DocumentDto(id, null, new RecordPayload(), ContractDocumentStatus.Posted, false));

        var resolver = new Mock<IDocumentReferenceRegisterPostingActionResolver>(MockBehavior.Strict);
        resolver.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((DocumentRecord document) => state.ResolveAction?.Invoke(document));

        var applier = new Mock<IReferenceRegisterRecordsApplier>(MockBehavior.Strict);
        applier.Setup(x => x.ApplyRecordsForDocumentAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), ReferenceRegisterWriteOperation.Post,
                It.IsAny<IReadOnlyList<ReferenceRegisterRecordWrite>>(), false, It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, ReferenceRegisterWriteOperation, IReadOnlyList<ReferenceRegisterRecordWrite>, bool,
                CancellationToken>((registerId, documentId, _, records, _, _) =>
                state.Applies.Add((registerId, documentId, records)))
            .ReturnsAsync((Guid _, Guid _, ReferenceRegisterWriteOperation _,
                IReadOnlyList<ReferenceRegisterRecordWrite> records, bool _, CancellationToken _) =>
                state.ApplyResult(records));

        var postedDocumentReader = new Mock<ICrmPostedDocumentReader>(MockBehavior.Strict);
        postedDocumentReader.Setup(x => x.GetIdsMissingReferenceRegisterPostPageAfterAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, Guid _, Guid? _, Guid? afterId, int limit, CancellationToken _) =>
                state.GetBackfillPage(type, afterId, limit));

        return new CrmDemoSeedService(
            setup.Object,
            catalogs.Object,
            documents.Object,
            lifecycle.Object,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)),
            resolver.Object,
            applier.Object,
            postedDocumentReader.Object,
            state.UnitOfWork,
            new CrmDemoSeedOptions());
    }

    private static CrmDemoSeedService CreateServiceWithOptions(CrmDemoSeedOptions options) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, options);

    private static DocumentDto Document(ContractDocumentStatus status, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), null, new RecordPayload(), status, false);

    private static ReferenceRegisterRecordWrite Record(Guid? recorderId) =>
        new(Guid.CreateVersion7(), null, recorderId, new Dictionary<string, object?> { ["value"] = 1 });

    private static CatalogItemDto Catalog(string display, RecordPayload payload) =>
        new(Guid.CreateVersion7(), display, payload, false, false);

    private static RecordPayload Payload(params (string Key, object? Value)[] fields) =>
        new(fields.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(item.Value),
            StringComparer.OrdinalIgnoreCase));

    private static string? Display(RecordPayload payload) =>
        payload.Fields != null && payload.Fields.TryGetValue("display", out var value)
            ? value.GetString()
            : null;

    private sealed class SeedState
    {
        public SeedState()
        {
            AddDefault(CrmCodes.OpportunityStage, "Qualification");
            AddDefault(CrmCodes.OpportunityStage, "Proposal");
            AddDefault(CrmCodes.OpportunityStage, "Negotiation");
            AddDefault(CrmCodes.OpportunityStage, "Closed Won");
            AddDefault(CrmCodes.OpportunityStage, "Closed Lost");
            AddDefault(CrmCodes.Product, "Platform Subscription");
            AddDefault(CrmCodes.Product, "Implementation Package");
            UnitOfWork = new FakeUnitOfWork(() => GeneratedLeadCount);
        }

        public int GeneratedLeadCount { get; init; }
        public int OperationalTotal { get; init; }
        public int SetupCalls { get; set; }
        public Dictionary<string, CatalogItemDto> DefaultCatalogs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<CatalogItemDto>> LookupOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Queue<IReadOnlyList<CatalogItemDto>> CatalogEnsurePages { get; } = new();
        public List<(string Type, RecordPayload Payload)> CatalogCreates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> CatalogUpdates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> DocumentCreates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> DocumentUpdates { get; } = [];
        public List<(string Type, Guid Id)> Posts { get; } = [];
        public Dictionary<string, List<Guid>> BackfillDocumentIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Func<DocumentRecord, Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation,
            CancellationToken, Task>?>? ResolveAction { get; set; }
        public Func<IReadOnlyList<ReferenceRegisterRecordWrite>, ReferenceRegisterWriteResult> ApplyResult { get; set; } =
            _ => ReferenceRegisterWriteResult.Executed;
        public List<(Guid RegisterId, Guid DocumentId, IReadOnlyList<ReferenceRegisterRecordWrite> Records)> Applies { get; } = [];
        public FakeUnitOfWork UnitOfWork { get; }

        public void SetBackfillPage(string type, int offset, IReadOnlyList<DocumentDto> items)
        {
            if (!BackfillDocumentIds.TryGetValue(type, out var ids))
            {
                ids = [];
                BackfillDocumentIds[type] = ids;
            }

            if (offset < ids.Count)
                ids.RemoveRange(offset, ids.Count - offset);
            if (offset > ids.Count)
                return;
            ids.AddRange(items
                .Where(static item => item.Status == ContractDocumentStatus.Posted)
                .Select(static item => item.Id));
            ids.Sort();
        }

        public IReadOnlyList<Guid> GetBackfillPage(string type, Guid? afterId, int limit) =>
            BackfillDocumentIds.GetValueOrDefault(type)?
                .Where(id => !afterId.HasValue || id.CompareTo(afterId.Value) > 0)
                .Take(limit)
                .ToArray() ?? [];

        private void AddDefault(string type, string display) =>
            DefaultCatalogs[$"{type}|{display}"] = Catalog(display, new RecordPayload());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeUnitOfWork(Func<int> scalar) : IUnitOfWork
    {
        private readonly FakeDbConnection _connection = new(scalar);

        public DbConnection Connection => _connection;
        public DbTransaction? Transaction => null;
        public bool HasActiveTransaction => false;
        public int BeginCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public Task EnsureConnectionOpenAsync(CancellationToken ct = default)
        {
            _connection.Open();
            return Task.CompletedTask;
        }

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginCount++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public void EnsureActiveTransaction() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDbConnection(Func<int> scalar) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull]
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this, scalar);
    }

    private sealed class FakeDbCommand(DbConnection connection, Func<int> scalar) : DbCommand
    {
        private readonly FakeDbParameterCollection _parameters = new();
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object ExecuteScalar() => scalar();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => ((ICollection)_items).SyncRoot;
        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }
        public override void AddRange(Array values)
        {
            foreach (var value in values) Add(value!);
        }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) =>
            _items.FindIndex(item => string.Equals(item.ParameterName, parameterName, StringComparison.Ordinal));
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0) _items.Add(value);
            else _items[index] = value;
        }
    }
}
