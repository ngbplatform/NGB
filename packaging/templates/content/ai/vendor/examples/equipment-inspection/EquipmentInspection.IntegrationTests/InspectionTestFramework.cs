using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("EquipmentInspection.IntegrationTests.InspectionTestFramework", "EquipmentInspection.IntegrationTests")]

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new InspectionTestExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

internal sealed class InspectionTestExecutor(
    AssemblyName assemblyName,
    ISourceInformationProvider sourceInformationProvider,
    IMessageSink diagnosticMessageSink)
    : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    // xUnit v2 defines this execution hook as async void.
    protected override async void RunTestCases(
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        using var runner = new InspectionTestAssemblyRunner(
            TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
        await runner.RunAsync();
    }
}

internal sealed class InspectionTestAssemblyRunner(
    ITestAssembly testAssembly,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions)
    : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
{
    protected override async Task BeforeTestAssemblyFinishedAsync()
    {
        await Aggregator.RunAsync(TestInfrastructure.ShutdownAsync);
        await base.BeforeTestAssemblyFinishedAsync();
    }
}
