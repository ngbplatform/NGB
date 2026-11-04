using Ngb.Validation;
using System.Text.Json;
using Xunit;

namespace NgbApplication.Tests;

public sealed class AssemblyInspectionTests
{
    [Fact]
    public void Metadata_inspection_reads_public_symbols_and_references_without_loading_code()
    {
        using var stream = File.OpenRead(typeof(AssemblyInspector).Assembly.Location);
        var report = AssemblyInspector.Inspect(stream);

        Assert.Contains(report.PublicContracts, contract => contract.Type == typeof(AssemblyInspector).FullName
            && contract.Name == nameof(AssemblyInspector.Inspect));
        Assert.Contains(report.References, reference => reference == "System.Reflection.Metadata");
        Assert.DoesNotContain(report.PublicContracts, contract => contract.Name == "QualifiedName");
        Assert.Equal(64, report.Sha256.Length);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Invalid_assembly_bytes_are_rejected()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        Assert.Throws<BadImageFormatException>(() => AssemblyInspector.Inspect(stream));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 67108865)]
    public void Unbounded_or_non_seekable_input_is_rejected(bool seekable, long length)
    {
        using var stream = new InspectionStream(seekable, length);
        Assert.Throws<InvalidDataException>(() => AssemblyInspector.Inspect(stream));
    }

    [Fact]
    public void Command_returns_machine_readable_metadata_and_actionable_exit_codes()
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var path = typeof(AssemblyInspector).Assembly.Location;

        Assert.Equal(0, Ngb.Validation.Program.Run(["--assembly", path], output, errors));
        using var report = JsonDocument.Parse(output.ToString());
        Assert.Equal(typeof(AssemblyInspector).Assembly.GetName().Name, report.RootElement.GetProperty("name").GetString());
        Assert.Equal(string.Empty, errors.ToString());
        Assert.Equal(1, Ngb.Validation.Program.Run([], output, errors));
        Assert.Equal(1, Ngb.Validation.Program.Run(["--execute", path], output, errors));
        Assert.Equal(2, Ngb.Validation.Program.Run(["--assembly", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll")], output, errors));
        Assert.Equal(1, Ngb.Validation.Program.Main([]));
    }

    private sealed class InspectionStream(bool seekable, long length) : MemoryStream
    {
        public override bool CanSeek => seekable;
        public override long Length => length;
    }
}
