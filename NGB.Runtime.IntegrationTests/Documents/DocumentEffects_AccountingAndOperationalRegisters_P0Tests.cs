using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentEffects_AccountingAndOperationalRegisters_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_acc_opreg_effects";
    private const string HeadTable = "it_doc_acc_opreg_effects";

    [Fact]
    public async Task GetEffectsAsync_WhenPostedDocumentHasAccountingAndOperationalRegisters_ReturnsBothFamilies()
    {
        var state = new TestEffectsState();
        using var host = CreateHost(state);

        await EnsureHeadTableAsync(host);
        await SeedMinimalCoAAsync(host);
        var registerCode = "opreg_effects_a";
        await CreateAndConfigureRegisterAsync(host, registerCode);
        var emptySetId = await GetEmptyDimensionSetIdAsync(host);
        var postingDayUtc = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var documentId = await CreateDraftAsync(host, "IT-EFX-001", postingDayUtc, "IT Effects 001");

        state.SetAccountingEntries(documentId,
        [
            new TestAccountingEffect("50", "90.1", 100m, postingDayUtc),
            new TestAccountingEffect("91", "50", 40m, postingDayUtc)
        ]);
        state.SetOperationalMovements(documentId,
        [
            new TestOperationalEffect(registerCode, emptySetId, postingDayUtc, 11m),
            new TestOperationalEffect(registerCode, emptySetId, postingDayUtc, 22m)
        ]);

        await PostAsync(host, documentId);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var effects = await documents.GetEffectsAsync(DocTypeCode, documentId, 100, CancellationToken.None);

        effects.AccountingEntries.Should().HaveCount(2);
        effects.AccountingEntries.Should().ContainSingle(x => x.DebitAccount.Code == "50" && x.CreditAccount.Code == "90.1" && x.Amount == 100m);
        effects.AccountingEntries.Should().ContainSingle(x => x.DebitAccount.Code == "91" && x.CreditAccount.Code == "50" && x.Amount == 40m);

        effects.OperationalRegisterMovements.Should().HaveCount(2);
        effects.OperationalRegisterMovements.Should().OnlyContain(x => x.RegisterCode == registerCode && x.RegisterName == "IT Effects Register" && x.DocumentId == documentId);
        effects.OperationalRegisterMovements.SelectMany(x => x.Resources).Should().Contain(x => x.Code == "amount" && x.Value == 11m);
        effects.OperationalRegisterMovements.SelectMany(x => x.Resources).Should().Contain(x => x.Code == "amount" && x.Value == 22m);
        effects.OperationalRegisterMovements.Should().OnlyContain(x => x.Dimensions.Count == 0);

        effects.ReferenceRegisterWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEffectsAsync_WhenDocumentIsNotPosted_ReturnsNoEffects()
    {
        var state = new TestEffectsState();
        using var host = CreateHost(state);

        await EnsureHeadTableAsync(host);
        await SeedMinimalCoAAsync(host);
        var registerCode = "opreg_effects_draft";
        await CreateAndConfigureRegisterAsync(host, registerCode);
        var emptySetId = await GetEmptyDimensionSetIdAsync(host);
        var postingDayUtc = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);
        var documentId = await CreateDraftAsync(host, "IT-EFX-002", postingDayUtc, "IT Effects 002");

        state.SetAccountingEntries(documentId, [new TestAccountingEffect("50", "90.1", 100m, postingDayUtc)]);
        state.SetOperationalMovements(documentId, [new TestOperationalEffect(registerCode, emptySetId, postingDayUtc, 11m)]);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var effects = await documents.GetEffectsAsync(DocTypeCode, documentId, 100, CancellationToken.None);

        effects.AccountingEntries.Should().BeEmpty();
        effects.OperationalRegisterMovements.Should().BeEmpty();
        effects.ReferenceRegisterWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEffectsAsync_AfterPostUnpostPost_ReturnsOnlyCurrentEffectiveSnapshot()
    {
        var state = new TestEffectsState();
        using var host = CreateHost(state);

        await EnsureHeadTableAsync(host);
        await SeedMinimalCoAAsync(host);
        var registerCode = "opreg_effects_repost";
        await CreateAndConfigureRegisterAsync(host, registerCode);
        var emptySetId = await GetEmptyDimensionSetIdAsync(host);
        var documentDayUtc = new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc);
        var documentId = await CreateDraftAsync(host, "IT-EFX-003", documentDayUtc, "IT Effects 003");

        state.SetAccountingEntries(documentId, [new TestAccountingEffect("50", "90.1", 100m, documentDayUtc)]);
        state.SetOperationalMovements(documentId, [new TestOperationalEffect(registerCode, emptySetId, documentDayUtc, 11m)]);
        await PostAsync(host, documentId);
        await UnpostAsync(host, documentId);

        state.SetAccountingEntries(documentId, [new TestAccountingEffect("91", "50", 200m, documentDayUtc)]);
        state.SetOperationalMovements(documentId, [new TestOperationalEffect(registerCode, emptySetId, documentDayUtc, 22m)]);
        await PostAsync(host, documentId);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var effects = await documents.GetEffectsAsync(DocTypeCode, documentId, 100, CancellationToken.None);

        effects.AccountingEntries.Should().ContainSingle();
        effects.AccountingEntries.Single().DebitAccount.Code.Should().Be("91");
        effects.AccountingEntries.Single().CreditAccount.Code.Should().Be("50");
        effects.AccountingEntries.Single().Amount.Should().Be(200m);

        effects.OperationalRegisterMovements.Should().ContainSingle();
        effects.OperationalRegisterMovements.Single().Resources.Should().ContainSingle(x => x.Code == "amount" && x.Value == 22m);
    }

    [Fact]
    public async Task GetEffectsAsync_AppliesLimitPerEffectFamily()
    {
        var state = new TestEffectsState();
        using var host = CreateHost(state);

        await EnsureHeadTableAsync(host);
        await SeedMinimalCoAAsync(host);
        var registerCode = "opreg_effects_limit";
        await CreateAndConfigureRegisterAsync(host, registerCode);
        var emptySetId = await GetEmptyDimensionSetIdAsync(host);
        var postingDayUtc = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
        var documentId = await CreateDraftAsync(host, "IT-EFX-004", postingDayUtc, "IT Effects 004");

        state.SetAccountingEntries(documentId,
        [
            new TestAccountingEffect("50", "90.1", 100m, postingDayUtc),
            new TestAccountingEffect("91", "50", 40m, postingDayUtc),
            new TestAccountingEffect("50", "90.1", 30m, postingDayUtc)
        ]);
        state.SetOperationalMovements(documentId,
        [
            new TestOperationalEffect(registerCode, emptySetId, postingDayUtc, 11m),
            new TestOperationalEffect(registerCode, emptySetId, postingDayUtc, 22m)
        ]);
        await PostAsync(host, documentId);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var effects = await documents.GetEffectsAsync(DocTypeCode, documentId, 1, CancellationToken.None);

        effects.AccountingEntries.Should().HaveCount(1);
        effects.OperationalRegisterMovements.Should().HaveCount(1);
        effects.ReferenceRegisterWrites.Should().BeEmpty();
    }

    private IHost CreateHost(TestEffectsState state)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton<TestAccountingEffectsPostingHandler>();
                services.AddSingleton<IDocumentPostingHandler>(sp => sp.GetRequiredService<TestAccountingEffectsPostingHandler>());
                services.AddSingleton<TestOperationalEffectsPostingHandler>();
                services.AddSingleton<IDocumentOperationalRegisterPostingHandler>(sp => sp.GetRequiredService<TestOperationalEffectsPostingHandler>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor>(new TestEffectsDefinitionsContributor()));
            });

    private static async Task EnsureHeadTableAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(
                $$"""
                CREATE TABLE IF NOT EXISTS {{HeadTable}} (
                    document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                    display text NOT NULL
                );
                """,
                transaction: uow.Transaction);
        }, CancellationToken.None);
    }

    private static async Task SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task CreateAndConfigureRegisterAsync(IHost host, string registerCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await management.UpsertAsync(registerCode, "IT Effects Register", CancellationToken.None);
        await management.ReplaceResourcesAsync(registerId, [new OperationalRegisterResourceDefinition("amount", "Amount", 1)], CancellationToken.None);
    }

    private static async Task<Guid> GetEmptyDimensionSetIdAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dimensionSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        return await dimensionSets.GetOrCreateIdAsync(DimensionBag.Empty, CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, string number, DateTime dateUtc, string display)
    {
        var id = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = DocTypeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await uow.Connection.ExecuteAsync(
                $$"""
                INSERT INTO {{HeadTable}}(document_id, display)
                VALUES (@DocumentId, @Display);
                """,
                new { DocumentId = id, Display = display },
                transaction: uow.Transaction);
        }, CancellationToken.None);

        return id;
    }

    private static async Task PostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await posting.PostAsync(documentId, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await posting.UnpostAsync(documentId, CancellationToken.None);
    }

    private sealed record TestAccountingEffect(string DebitCode, string CreditCode, decimal Amount, DateTime OccurredAtUtc);

    private sealed record TestOperationalEffect(string RegisterCode, Guid DimensionSetId, DateTime OccurredAtUtc, decimal Amount);

    private sealed class TestEffectsState
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, TestAccountingEffect[]> _accountingByDocumentId = new();
        private readonly Dictionary<Guid, TestOperationalEffect[]> _operationalByDocumentId = new();

        public void SetAccountingEntries(Guid documentId, TestAccountingEffect[] rows)
        {
            lock (_gate)
                _accountingByDocumentId[documentId] = rows;
        }

        public void SetOperationalMovements(Guid documentId, TestOperationalEffect[] rows)
        {
            lock (_gate)
                _operationalByDocumentId[documentId] = rows;
        }

        public IReadOnlyList<TestAccountingEffect> GetAccountingEntries(Guid documentId)
        {
            lock (_gate)
                return _accountingByDocumentId.TryGetValue(documentId, out var rows) ? rows : [];
        }

        public IReadOnlyList<TestOperationalEffect> GetOperationalMovements(Guid documentId)
        {
            lock (_gate)
                return _operationalByDocumentId.TryGetValue(documentId, out var rows) ? rows : [];
        }
    }

    private sealed class TestAccountingEffectsPostingHandler(TestEffectsState state) : IDocumentPostingHandler
    {
        public string TypeCode => DocTypeCode;

        public async Task BuildEntriesAsync(DocumentRecord document, NGB.Accounting.Posting.IAccountingPostingContext ctx, CancellationToken ct)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            foreach (var row in state.GetAccountingEntries(document.Id))
            {
                ctx.Post(document.Id, row.OccurredAtUtc, chart.Get(row.DebitCode), chart.Get(row.CreditCode), row.Amount);
            }
        }
    }

    private sealed class TestOperationalEffectsPostingHandler(TestEffectsState state) : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => DocTypeCode;

        public Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
        {
            foreach (var row in state.GetOperationalMovements(document.Id))
            {
                builder.Add(row.RegisterCode, new OperationalRegisterMovement(document.Id, row.OccurredAtUtc, row.DimensionSetId, new Dictionary<string, decimal>
                {
                    ["amount"] = row.Amount
                }));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestEffectsDefinitionsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(DocTypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    DocTypeCode,
                    [
                        new DocumentTableMetadata(
                            HeadTable,
                            TableKind.Head,
                            [
                                new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                                new DocumentColumnMetadata("display", ColumnType.String, Required: true),
                            ])
                    ],
                    new DocumentPresentationMetadata("IT Accounting + OpReg Effects"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .PostingHandler<TestAccountingEffectsPostingHandler>()
                .OperationalRegisterPostingHandler<TestOperationalEffectsPostingHandler>());
        }
    }
}
