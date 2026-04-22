using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Core.AuditLog;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportVariantService_P0Tests
{
    [Fact]
    public async Task SaveAsync_PrivateVariant_Requires_Current_User_Context()
    {
        var sut = CreateSut(new InMemoryReportVariantRepository(), actor: null);

        var act = async () => await sut.SaveAsync(
            new ReportVariantDto(
                VariantCode: "tenant-view",
                ReportCode: "accounting.ledger.analysis",
                Name: "Tenant View",
                Layout: new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
                Parameters: new Dictionary<string, string>
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                IsDefault: false,
                IsShared: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<ReportVariantValidationException>();
    }

    [Fact]
    public async Task GetAllAsync_Returns_Shared_And_Owned_Private_Variants_And_Hides_Others()
    {
        var repository = new InMemoryReportVariantRepository();

        var shared = CreateSut(repository, actor: null);
        await shared.SaveAsync(CreateVariant("shared-view", "Shared View", isShared: true, isDefault: false), CancellationToken.None);

        var owner1 = CreateSut(repository, new StubActorContext("user-1"));
        await owner1.SaveAsync(CreateVariant("owner-view", "Owner View", isShared: false, isDefault: true), CancellationToken.None);

        var owner2 = CreateSut(repository, new StubActorContext("user-2"));
        var visibleForUser2 = await owner2.GetAllAsync("accounting.ledger.analysis", CancellationToken.None);
        visibleForUser2.Select(x => x.VariantCode).Should().BeEquivalentTo(["shared-view"]);

        var visibleForUser1 = await owner1.GetAllAsync("accounting.ledger.analysis", CancellationToken.None);
        visibleForUser1.Select(x => x.VariantCode).Should().BeEquivalentTo(["owner-view", "shared-view"]);
        visibleForUser1.Single(x => x.VariantCode == "owner-view").IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_New_Default_Clears_Previous_Default_In_Same_Scope()
    {
        var repository = new InMemoryReportVariantRepository();
        var sut = CreateSut(repository, actor: null);

        await sut.SaveAsync(CreateVariant("default-a", "Default A", isShared: true, isDefault: true), CancellationToken.None);
        await sut.SaveAsync(CreateVariant("default-b", "Default B", isShared: true, isDefault: true), CancellationToken.None);

        var variants = await sut.GetAllAsync("accounting.ledger.analysis", CancellationToken.None);
        variants.Single(x => x.VariantCode == "default-a").IsDefault.Should().BeFalse();
        variants.Single(x => x.VariantCode == "default-b").IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_SharedVariant_With_Current_User_Persists_Owner()
    {
        var repository = new InMemoryReportVariantRepository();
        var sut = CreateSut(repository, new StubActorContext("admin-1"));

        await sut.SaveAsync(CreateVariant("shared-admin-view", "Shared Admin View", isShared: true, isDefault: false), CancellationToken.None);

        var stored = (await repository.ListByCodeAsync("accounting.ledger.analysis", "shared-admin-view", CancellationToken.None)).Single();
        stored.Should().NotBeNull();
        stored!.OwnerPlatformUserId.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAsync_PrivateCode_Can_Be_Reused_By_Different_Owners()
    {
        var repository = new InMemoryReportVariantRepository();

        var owner1 = CreateSut(repository, new StubActorContext("user-1"));
        var owner2 = CreateSut(repository, new StubActorContext("user-2"));

        await owner1.SaveAsync(CreateVariant("tenant-view", "Tenant View", isShared: false, isDefault: false), CancellationToken.None);
        await owner2.SaveAsync(CreateVariant("tenant-view", "Tenant View 2", isShared: false, isDefault: false), CancellationToken.None);

        var stored = await repository.ListByCodeAsync("accounting.ledger.analysis", "tenant-view", CancellationToken.None);
        stored.Should().HaveCount(2);
        stored.Select(x => x.OwnerPlatformUserId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SaveAsync_PrivateCode_Conflicts_With_Shared_Code()
    {
        var repository = new InMemoryReportVariantRepository();

        var shared = CreateSut(repository, new StubActorContext("admin-1"));
        var owner = CreateSut(repository, new StubActorContext("user-1"));

        await shared.SaveAsync(CreateVariant("shared-view", "Shared View", isShared: true, isDefault: false), CancellationToken.None);

        var act = async () => await owner.SaveAsync(CreateVariant("shared-view", "Private View", isShared: false, isDefault: false), CancellationToken.None);

        await act.Should().ThrowAsync<ReportVariantCodeConflictException>();
    }

    private static ReportVariantDto CreateVariant(string variantCode, string name, bool isShared, bool isDefault)
        => new(
            VariantCode: variantCode,
            ReportCode: "accounting.ledger.analysis",
            Name: name,
            Layout: new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            IsDefault: isDefault,
            IsShared: isShared);

    private static ReportVariantService CreateSut(InMemoryReportVariantRepository repository, StubActorContext? actor)
        => new(repository, new StubDefinitions(), actor ?? new StubActorContext(), new InMemoryPlatformUserRepository());

    private sealed class StubDefinitions : IReportDefinitionProvider
    {
        private readonly ReportDefinitionDto _definition = new AccountingLedgerAnalysisDefinitionSource().GetDefinitions().Single();

        public Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportDefinitionDto>>([_definition]);

        public Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct)
            => Task.FromResult(_definition);
    }

    private sealed class StubActorContext : IReportVariantAccessContext
    {
        public StubActorContext(string? authSubject = null)
        {
            AuthSubject = authSubject;
            Email = authSubject is null ? null : $"{authSubject}@example.test";
            DisplayName = authSubject;
            IsActive = authSubject is not null;
        }

        public string? AuthSubject { get; }

        public string? Email { get; }

        public string? DisplayName { get; }

        public bool IsActive { get; }
    }

    private sealed class InMemoryPlatformUserRepository : IPlatformUserRepository
    {
        private readonly Dictionary<string, PlatformUser> _byAuthSubject = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, PlatformUser> _byId = new();
        private readonly Dictionary<string, Guid> _stableIds = new(StringComparer.Ordinal);

        public Task<Guid> UpsertAsync(string authSubject, string? email, string? displayName, bool isActive, CancellationToken ct = default)
        {
            var normalized = authSubject.Trim();
            var userId = _stableIds.GetValueOrDefault(normalized);
            if (userId == Guid.Empty)
            {
                userId = Guid.CreateVersion7();
                _stableIds[normalized] = userId;
            }

            var nowUtc = DateTime.UtcNow;
            var createdAtUtc = _byAuthSubject.TryGetValue(normalized, out var existing)
                ? existing.CreatedAtUtc
                : nowUtc;

            var user = new PlatformUser(
                UserId: userId,
                AuthSubject: normalized,
                Email: email,
                DisplayName: displayName,
                IsActive: isActive,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: nowUtc);

            _byAuthSubject[normalized] = user;
            _byId[userId] = user;
            return Task.FromResult(userId);
        }

        public Task<PlatformUser?> GetByAuthSubjectAsync(string authSubject, CancellationToken ct = default)
            => Task.FromResult(_byAuthSubject.GetValueOrDefault(authSubject.Trim()));

        public Task<IReadOnlyDictionary<Guid, PlatformUser>> GetByIdsAsync(IReadOnlyList<Guid> userIds, CancellationToken ct = default)
        {
            var result = userIds
                .Where(userId => _byId.ContainsKey(userId))
                .Distinct()
                .ToDictionary(userId => userId, userId => _byId[userId]);

            return Task.FromResult<IReadOnlyDictionary<Guid, PlatformUser>>(result);
        }
    }

    private sealed class InMemoryReportVariantRepository : IReportVariantRepository
    {
        private readonly List<ReportVariantRecord> _rows = [];

        public Task<IReadOnlyList<ReportVariantRecord>> ListVisibleAsync(string reportCodeNorm, Guid? currentUserId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportVariantRecord>>(_rows
                .Where(x => x.ReportCodeNorm == reportCodeNorm && (x.IsShared || (currentUserId.HasValue && x.OwnerPlatformUserId == currentUserId)))
                .OrderByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.IsShared)
                .ThenBy(x => x.Name)
                .ToList());

        public Task<ReportVariantRecord?> GetVisibleAsync(string reportCodeNorm, string variantCodeNorm, Guid? currentUserId, CancellationToken ct)
            => Task.FromResult(_rows.SingleOrDefault(x => x.ReportCodeNorm == reportCodeNorm && x.VariantCodeNorm == variantCodeNorm && (x.IsShared || (currentUserId.HasValue && x.OwnerPlatformUserId == currentUserId))));

        public Task<IReadOnlyList<ReportVariantRecord>> ListByCodeAsync(string reportCodeNorm, string variantCodeNorm, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportVariantRecord>>(_rows
                .Where(x => x.ReportCodeNorm == reportCodeNorm && x.VariantCodeNorm == variantCodeNorm)
                .OrderByDescending(x => x.IsShared)
                .ThenBy(x => x.CreatedAtUtc)
                .ToList());

        public Task ClearDefaultAsync(string reportCodeNorm, Guid? ownerPlatformUserId, bool isShared, string? exceptVariantCodeNorm, CancellationToken ct)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (row.ReportCodeNorm != reportCodeNorm) continue;
                if (row.IsShared != isShared) continue;
                if (!isShared && row.OwnerPlatformUserId != ownerPlatformUserId) continue;
                if (!string.IsNullOrWhiteSpace(exceptVariantCodeNorm) && row.VariantCodeNorm == exceptVariantCodeNorm) continue;
                if (!row.IsDefault) continue;
                _rows[i] = row with { IsDefault = false, UpdatedAtUtc = DateTime.UtcNow };
            }

            return Task.CompletedTask;
        }

        public Task<ReportVariantRecord> UpsertAsync(ReportVariantRecord record, CancellationToken ct)
        {
            var index = _rows.FindIndex(x => x.ReportVariantId == record.ReportVariantId);
            if (index >= 0) _rows[index] = record;
            else _rows.Add(record);
            return Task.FromResult(record);
        }

        public Task<bool> DeleteVisibleAsync(string reportCodeNorm, string variantCodeNorm, Guid? currentUserId, CancellationToken ct)
        {
            var index = _rows.FindIndex(x => x.ReportCodeNorm == reportCodeNorm && x.VariantCodeNorm == variantCodeNorm && (x.IsShared || (currentUserId.HasValue && x.OwnerPlatformUserId == currentUserId)));
            if (index < 0) return Task.FromResult(false);
            _rows.RemoveAt(index);
            return Task.FromResult(true);
        }
    }
}
