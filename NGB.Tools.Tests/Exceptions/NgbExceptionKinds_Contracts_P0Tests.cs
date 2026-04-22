using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: All exception categories must map to stable NgbErrorKind values.
/// </summary>
public sealed class NgbExceptionKinds_Contracts_P0Tests
{
    [Fact]
    public void ConfigurationViolation_HasExpectedKindAndCode()
    {
        var ex = new NgbConfigurationViolationException("boom");

        ex.Kind.Should().Be(NgbErrorKind.Configuration);
        ex.ErrorCode.Should().Be(NgbConfigurationViolationException.Code);
    }

    [Fact]
    public void InvariantViolation_HasExpectedKindAndCode()
    {
        var ex = new NgbInvariantViolationException("boom");

        ex.Kind.Should().Be(NgbErrorKind.Infrastructure);
        ex.ErrorCode.Should().Be(NgbInvariantViolationException.Code);
    }

    [Fact]
    public void AbstractCategories_MapToExpectedKinds()
    {
        new TestValidation("m", "it.validation").Kind.Should().Be(NgbErrorKind.Validation);
        new TestNotFound("m", "it.not_found").Kind.Should().Be(NgbErrorKind.NotFound);
        new TestConflict("m", "it.conflict").Kind.Should().Be(NgbErrorKind.Conflict);
        new TestForbidden("m", "it.forbidden").Kind.Should().Be(NgbErrorKind.Forbidden);
        new TestInfrastructure("m", "it.infra").Kind.Should().Be(NgbErrorKind.Infrastructure);
        new TestConfiguration("m", "it.config").Kind.Should().Be(NgbErrorKind.Configuration);
    }

    private sealed class TestValidation(string message, string errorCode)
        : NgbValidationException(message, errorCode);

    private sealed class TestNotFound(string message, string errorCode)
        : NgbNotFoundException(message, errorCode);

    private sealed class TestConflict(string message, string errorCode)
        : NgbConflictException(message, errorCode);

    private sealed class TestForbidden(string message, string errorCode)
        : NgbForbiddenException(message, errorCode);

    private sealed class TestInfrastructure(string message, string errorCode)
        : NgbInfrastructureException(message, errorCode);

    private sealed class TestConfiguration(string message, string errorCode)
        : NgbConfigurationException(message, errorCode);
}
