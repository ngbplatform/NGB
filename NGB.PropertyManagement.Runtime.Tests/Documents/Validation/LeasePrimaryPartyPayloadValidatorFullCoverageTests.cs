using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class LeasePrimaryPartyPayloadValidatorFullCoverageTests
{
    [Fact]
    public async Task Part_presence_rules_cover_create_and_partial_update_boundaries()
    {
        var validator = CreateValidator();
        validator.TypeCode.Should().Be(PropertyManagementCodes.Lease);

        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(
            new RecordPayload(),
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(),
            default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(), default));
        await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), Rows(), default);
        await validator.ValidateUpdateDraftPayloadAsync(
            Guid.CreateVersion7(),
            new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>()),
            Rows(),
            default);
        await AssertInvalid(() => validator.ValidateUpdateDraftPayloadAsync(
            Guid.CreateVersion7(),
            new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>
            {
                ["parties"] = new([])
            }),
            Rows(),
            default));
    }

    [Fact]
    public async Task Tenant_rows_reject_empty_duplicate_and_invalid_primary_shapes()
    {
        var validator = CreateValidator();
        var partyId = Guid.CreateVersion7();

        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: true, role: "PrimaryTenant", ordinal: 1),
            Row(partyId, primary: false, role: "Occupant", ordinal: 2)), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: false, role: "Occupant", ordinal: 1)), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: true, role: "PrimaryTenant", ordinal: 1),
            Row(Guid.CreateVersion7(), primary: true, role: "PrimaryTenant", ordinal: 2)), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: true, role: "Occupant", ordinal: 1)), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: true, role: "PrimaryTenant", ordinal: 1),
            Row(Guid.CreateVersion7(), primary: false, role: "PrimaryTenant", ordinal: 2)), default));
        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(partyId, primary: true, role: "PrimaryTenant", ordinal: 1),
            Row(Guid.CreateVersion7(), primary: false, role: "Occupant", ordinal: 1)), default));
    }

    [Fact]
    public async Task Tenant_rows_accept_valid_roles_case_insensitively_and_ignore_schema_invalid_optional_values()
    {
        var validator = CreateValidator();
        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(Guid.CreateVersion7(), primary: true, role: "primarytenant", ordinal: 1),
            Row(Guid.CreateVersion7(), primary: false, role: "Occupant", ordinal: 2)), default);

        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            new Dictionary<string, object?>
            {
                ["party_id"] = Guid.Empty,
                ["is_primary"] = true,
                ["role"] = 42,
                ["ordinal"] = "not-an-int"
            }), default);

        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            new Dictionary<string, object?>
            {
                ["party_id"] = "schema-validates-this",
                ["is_primary"] = true
            }), default);

        await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            new Dictionary<string, object?>
            {
                ["is_primary"] = true,
                ["role"] = "PrimaryTenant"
            }), default);

        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(Guid.CreateVersion7(), primary: true, role: "Occupant", ordinal: 1),
            new Dictionary<string, object?> { ["role"] = "PrimaryTenant", ["ordinal"] = 2 }), default));

        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            Row(Guid.CreateVersion7(), primary: true, role: "Occupant", ordinal: 1),
            new Dictionary<string, object?>
            {
                ["is_primary"] = 1,
                ["role"] = "PrimaryTenant",
                ["ordinal"] = 2
            }), default));

        await AssertInvalid(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), Rows(
            new Dictionary<string, object?>
            {
                ["party_id"] = Guid.Empty,
                ["is_primary"] = 1,
                ["role"] = "Occupant"
            }), default));
    }

    [Fact]
    public async Task Tenant_role_guard_is_applied_to_every_non_empty_tenant_id()
    {
        var partyId = Guid.CreateVersion7();
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementParty?)null);
        var validator = new LeasePrimaryPartyPayloadValidator(parties.Object);

        await ((Func<Task>)(() => validator.ValidateCreateDraftPayloadAsync(
                new RecordPayload(),
                Rows(Row(partyId, primary: true, role: "PrimaryTenant", ordinal: 1)),
                default)))
            .Should().ThrowAsync<DocumentPartyValidationException>();
    }

    private static LeasePrimaryPartyPayloadValidator CreateValidator()
    {
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PropertyManagementParty(id, "Tenant", true, false, false));
        return new LeasePrimaryPartyPayloadValidator(parties.Object);
    }

    private static Dictionary<string, object?> Row(Guid partyId, bool primary, object? role, object? ordinal)
        => new(StringComparer.Ordinal)
        {
            ["party_id"] = partyId,
            ["is_primary"] = primary,
            ["role"] = role,
            ["ordinal"] = ordinal
        };

    private static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Rows(
        params IReadOnlyDictionary<string, object?>[] rows)
        => new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal)
        {
            ["parties"] = rows
        };

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<LeasePrimaryPartyPayloadValidationException>();
}
