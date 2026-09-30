using System.Reflection;
using FluentAssertions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PostValidatorBindingMatrixTests
{
    [Fact]
    public async Task Every_pm_post_validator_exposes_type_code_and_rejects_wrong_binding_before_dependencies_are_used()
    {
        var validators = typeof(LateFeeChargePostValidator).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IDocumentPostValidator).IsAssignableFrom(type))
            .Where(type => type.Namespace == typeof(LateFeeChargePostValidator).Namespace)
            .OrderBy(type => type.FullName)
            .Select(CreateWithoutCallingDependencies)
            .ToArray();

        validators.Should().HaveCount(15);
        foreach (var validator in validators)
        {
            validator.TypeCode.Should().StartWith("pm.");
            var wrongDocument = new DocumentRecord
            {
                Id = Guid.CreateVersion7(),
                TypeCode = "pm.wrong_type",
                DateUtc = DateTime.UnixEpoch,
                Status = DocumentStatus.Draft
            };

            await ((Func<Task>)(() => validator.ValidateBeforePostAsync(wrongDocument, default)))
                .Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }

    private static IDocumentPostValidator CreateWithoutCallingDependencies(Type type)
    {
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (IDocumentPostValidator)constructor.Invoke(new object?[constructor.GetParameters().Length]);
    }
}
