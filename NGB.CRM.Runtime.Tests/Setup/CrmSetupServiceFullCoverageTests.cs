using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Security;
using NGB.Contracts.Common;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.Security;
using NGB.Tools.Extensions;

namespace NGB.CRM.Runtime.Tests.Setup;

public sealed class CrmSetupServiceFullCoverageTests
{
    [Fact]
    public async Task EnsureDefaultsAsync_FirstRunCreatesAllRegistersCatalogsRolesAndAdministrator()
    {
        var state = new SetupState();
        var sut = CreateService(state);

        var result = await sut.EnsureDefaultsAsync();

        result.OpportunityStagesEnsured.Should().Be(6);
        result.ProductsEnsured.Should().Be(2);
        state.Registers.Should().HaveCount(4);
        state.Fields.Should().HaveCount(4);
        state.DimensionRules.Should().HaveCount(4);
        state.EnsuredSchemas.Should().HaveCount(4);
        state.CatalogCreates.Should().HaveCount(8);
        state.RoleCreates.Select(request => request.Code)
            .Should().Equal("crm.administrator", "crm.manager", "crm.sales_rep");
        state.UserUpserts.Should().ContainSingle().Which.Should().Be((
            "6d49204b-867c-4180-a30d-a5e290e13c73",
            "alex.carter@demo.ngbplatform.com",
            "Alex Carter"));
        state.AssignedRoles.Should().ContainSingle();
        state.AccessVersionUsers.Should().ContainSingle();
        state.BeginCount.Should().Be(1);
        state.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_UpdatesExistingCatalogsAndMergesMissingRolePermissions()
    {
        var state = new SetupState();
        await CreateService(state).EnsureDefaultsAsync();
        state.EnqueueDisplayMatches();
        foreach (var roleId in state.RoleDetails.Keys.ToArray())
            state.RoleDetails[roleId] = state.RoleDetails[roleId] with { Permissions = [] };

        var result = await CreateService(state).EnsureDefaultsAsync();

        result.OpportunityStagesEnsured.Should().Be(0);
        result.ProductsEnsured.Should().Be(0);
        state.CatalogUpdates.Should().HaveCount(8);
        state.RoleReplacements.Should().HaveCount(3);
        state.RoleReplacements.Should().OnlyContain(item => item.Permissions.Count > 0);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_WhenRolePermissionsAreComplete_DoesNotReplaceThem()
    {
        var state = new SetupState();
        await CreateService(state).EnsureDefaultsAsync();
        state.EnqueueDisplayMatches();
        var replacementsBefore = state.RoleReplacements.Count;

        await CreateService(state).EnsureDefaultsAsync();

        state.RoleReplacements.Should().HaveCount(replacementsBefore);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_MatchesByPayloadAndCoversAbsentAndWrongPayloadFields()
    {
        var state = new SetupState();
        state.CatalogPages.Enqueue([Catalog("Different", Payload(("stage_code", "PROSPECTING")))]);
        state.CatalogPages.Enqueue([new CatalogItemDto(Guid.CreateVersion7(), "Different", new RecordPayload(), false, false)]);
        state.CatalogPages.Enqueue([Catalog("Different", Payload(("other", "value")))]);
        state.CatalogPages.Enqueue([Catalog("Different", Payload(("stage_code", "WRONG")))]);
        for (var index = 0; index < 4; index++) state.CatalogPages.Enqueue([]);

        var result = await CreateService(state).EnsureDefaultsAsync();

        result.OpportunityStagesEnsured.Should().Be(5);
        result.ProductsEnsured.Should().Be(2);
        state.CatalogUpdates.Should().ContainSingle();
        state.CatalogCreates.Should().HaveCount(7);
    }

    [Fact]
    public async Task EnsureDefaultsAsync_RejectsDuplicateCatalogDefaults()
    {
        var duplicate = Catalog("Prospecting", Payload(("stage_code", "PROSPECTING")));
        var state = new SetupState();
        state.CatalogPages.Enqueue([duplicate, duplicate with { Id = Guid.CreateVersion7() }]);
        var act = () => CreateService(state).EnsureDefaultsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Multiple*");
        state.RoleCreates.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureDefaultsAsync_UsesConfiguredIdentityAndEmailWhenNamesAreBlank()
    {
        var state = new SetupState();

        await CreateService(
            state,
            new CrmDemoAdministratorOptions("custom-subject", "admin@example.test", " ", "\t"))
            .EnsureDefaultsAsync();

        state.UserUpserts.Should().ContainSingle().Which.Should().Be((
            "custom-subject", "admin@example.test", "admin@example.test"));
    }

    private static CrmSetupService CreateService(
        SetupState state,
        CrmDemoAdministratorOptions? demoAdministrator = null)
    {
        var registers = new Mock<IReferenceRegisterManagementService>(MockBehavior.Strict);
        registers.Setup(x => x.UpsertAsync(
                It.IsAny<string>(), It.IsAny<string>(), ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent, It.IsAny<CancellationToken>()))
            .Callback<string, string, ReferenceRegisterPeriodicity, ReferenceRegisterRecordMode, CancellationToken>(
                (code, _, _, _, _) => state.Registers.Add(code))
            .ReturnsAsync((string code, string _, ReferenceRegisterPeriodicity _, ReferenceRegisterRecordMode _,
                CancellationToken _) => DeterministicGuid.Create($"Register|{code}"));
        registers.Setup(x => x.ReplaceFieldsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReferenceRegisterFieldDefinition>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<ReferenceRegisterFieldDefinition>, CancellationToken>(
                (id, fields, _) => state.Fields.Add((id, fields)))
            .Returns(Task.CompletedTask);
        registers.Setup(x => x.ReplaceDimensionRulesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReferenceRegisterDimensionRule>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<ReferenceRegisterDimensionRule>, CancellationToken>(
                (id, rules, _) => state.DimensionRules.Add((id, rules)))
            .Returns(Task.CompletedTask);

        var maintenance = new Mock<IReferenceRegisterAdminMaintenanceService>(MockBehavior.Strict);
        maintenance.Setup(x => x.EnsurePhysicalSchemaByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => state.EnsuredSchemas.Add(id))
            .ReturnsAsync((ReferenceRegisterPhysicalSchemaHealth?)null);

        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, PageRequestDto request, CancellationToken _) =>
            {
                var items = state.CatalogPages.TryDequeue(out var page) ? page : [];
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

        var roles = new Mock<IRoleManagementService>(MockBehavior.Strict);
        roles.Setup(x => x.GetRolesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => state.Roles.ToArray());
        roles.Setup(x => x.CreateRoleAsync(It.IsAny<CreateRoleRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateRoleRequestDto, CancellationToken>((request, _) => state.AddRole(request))
            .ReturnsAsync((CreateRoleRequestDto request, CancellationToken _) =>
                state.RoleDetails[state.Roles.Single(role => role.Code == request.Code).RoleId]);
        roles.Setup(x => x.GetRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => state.RoleDetails[id]);
        roles.Setup(x => x.ReplaceRolePermissionsAsync(
                It.IsAny<Guid>(), It.IsAny<ReplaceRolePermissionsRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ReplaceRolePermissionsRequestDto, CancellationToken>((id, request, _) =>
            {
                state.RoleReplacements.Add((id, request.Permissions));
                state.RoleDetails[id] = state.RoleDetails[id] with { Permissions = request.Permissions };
            })
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => state.BeginCount++)
            .Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => state.CommitCount++)
            .Returns(Task.CompletedTask);

        var users = new Mock<IPlatformUserRepository>(MockBehavior.Strict);
        users.Setup(x => x.UpsertAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, bool, CancellationToken>(
                (subject, email, display, _, _) => state.UserUpserts.Add((subject, email, display)))
            .ReturnsAsync(state.UserId);

        var userRoles = new Mock<IPlatformUserRoleRepository>(MockBehavior.Strict);
        userRoles.Setup(x => x.ReplaceUserRolesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), null, It.IsAny<CancellationToken>()))
            .Callback<Guid, IReadOnlyList<Guid>, Guid?, CancellationToken>(
                (userId, roleIds, _, _) => state.AssignedRoles.Add((userId, roleIds)))
            .Returns(Task.CompletedTask);

        var versions = new Mock<IUserAccessVersionRepository>(MockBehavior.Strict);
        versions.Setup(x => x.GetOrCreateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((userId, _) => state.AccessVersionUsers.Add(userId))
            .ReturnsAsync((Guid userId, CancellationToken _) =>
                new PlatformUserAccessVersion(userId, 1, DateTime.UnixEpoch));

        return new CrmSetupService(
            registers.Object, maintenance.Object, catalogs.Object, roles.Object, uow.Object,
            users.Object, userRoles.Object, versions.Object,
            demoAdministrator ?? new CrmDemoAdministratorOptions());
    }

    private static CatalogItemDto Catalog(string display, RecordPayload payload) =>
        new(Guid.CreateVersion7(), display, payload, false, false);

    private static RecordPayload Payload(params (string Key, object? Value)[] fields)
    {
        var values = fields.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(item.Value),
            StringComparer.OrdinalIgnoreCase);
        return new RecordPayload(values);
    }

    private static string? Display(RecordPayload payload) =>
        payload.Fields != null && payload.Fields.TryGetValue("display", out var display)
            ? display.GetString()
            : null;

    private sealed class SetupState
    {
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Queue<IReadOnlyList<CatalogItemDto>> CatalogPages { get; } = new();
        public List<string> Registers { get; } = [];
        public List<(Guid Id, IReadOnlyList<ReferenceRegisterFieldDefinition> Fields)> Fields { get; } = [];
        public List<(Guid Id, IReadOnlyList<ReferenceRegisterDimensionRule> Rules)> DimensionRules { get; } = [];
        public List<Guid> EnsuredSchemas { get; } = [];
        public List<(string Type, RecordPayload Payload)> CatalogCreates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> CatalogUpdates { get; } = [];
        public List<RoleListItemDto> Roles { get; } = [];
        public Dictionary<Guid, RoleDetailsDto> RoleDetails { get; } = [];
        public List<CreateRoleRequestDto> RoleCreates { get; } = [];
        public List<(Guid RoleId, IReadOnlyList<PermissionAssignmentDto> Permissions)> RoleReplacements { get; } = [];
        public List<(string Subject, string? Email, string? Display)> UserUpserts { get; } = [];
        public List<(Guid UserId, IReadOnlyList<Guid> RoleIds)> AssignedRoles { get; } = [];
        public List<Guid> AccessVersionUsers { get; } = [];
        public int BeginCount { get; set; }
        public int CommitCount { get; set; }

        public void AddRole(CreateRoleRequestDto request)
        {
            RoleCreates.Add(request);
            var id = Guid.CreateVersion7();
            var now = DateTime.UnixEpoch;
            Roles.Add(new RoleListItemDto(id, request.Code, request.Name, request.Description, false, true, 0, now, now));
            RoleDetails[id] = new RoleDetailsDto(id, request.Code, request.Name, request.Description, false, true,
                request.Permissions, [], now, now);
        }

        public void EnqueueDisplayMatches()
        {
            foreach (var create in CatalogCreates.TakeLast(8))
                CatalogPages.Enqueue([Catalog(Display(create.Payload)!, create.Payload)]);
        }
    }
}
