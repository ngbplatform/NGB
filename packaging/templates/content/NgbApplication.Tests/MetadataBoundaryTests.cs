using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Ngb.Validation;
using Xunit;

namespace NgbApplication.Tests;

public sealed class MetadataBoundaryTests
{
    [Fact]
    public void Native_images_are_rejected_without_loading_them()
    {
        var bytes = File.ReadAllBytes(typeof(AssemblyInspector).Assembly.Location);
        using (var original = new PEReader(new MemoryStream(bytes)))
        {
            var peHeader = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3c, 4));
            var directories = peHeader + 24 + (original.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(directories + 14 * 8, 8), 0);
        }

        using var stream = new MemoryStream(bytes);
        var error = Assert.Throws<InvalidDataException>(() => AssemblyInspector.Inspect(stream));
        Assert.Contains("no managed metadata", error.Message);
    }

    [Fact]
    public void Excessive_type_inventories_and_nesting_fail_with_bounded_errors()
    {
        using var manyTypes = MetadataImage(100001, false);
        Assert.Contains("type inventory", Assert.Throws<InvalidDataException>(() => AssemblyInspector.Inspect(manyTypes)).Message);

        using var nestedTypes = MetadataImage(35, true);
        Assert.Contains("Nested type", Assert.Throws<InvalidDataException>(() => AssemblyInspector.Inspect(nestedTypes)).Message);
    }

    [Fact]
    public void Public_surface_contains_inheritable_members_and_excludes_internal_or_private_members()
    {
        using var stream = File.OpenRead(typeof(AssemblyInspector).Assembly.Location);
        var contracts = AssemblyInspector.Inspect(stream).PublicContracts;
        Assert.Contains(contracts, contract => contract.Type == nameof(GlobalMetadataSample));
        foreach (var name in new[] { "PublicValue", "ProtectedValue", "ProtectedInternalValue" })
        {
            Assert.Contains(contracts, contract => contract.Type == nameof(GlobalMetadataSample) && contract.Name == name);
        }
        foreach (var name in new[] { "PublicNested", "ProtectedNested", "ProtectedInternalNested" })
        {
            Assert.Contains(contracts, contract => contract.Type == nameof(GlobalMetadataSample) + "+" + name);
        }
        foreach (var name in new[] { "PrivateValue", "InternalValue", "PrivateNested", "InternalNested", "PrivateProtectedNested" })
        {
            Assert.DoesNotContain(contracts, contract => contract.Name == name
                || contract.Type == nameof(GlobalMetadataSample) + "+" + name);
        }
    }

    private static MemoryStream MetadataImage(int count, bool nested)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Boundary.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Boundary"), new Version(1, 0), default, default, default, AssemblyHashAlgorithm.None);
        TypeDefinitionHandle parent = default;
        for (var index = 0; index < count; index++)
        {
            var visibility = nested && index > 0 ? TypeAttributes.NestedPublic : TypeAttributes.Public;
            var handle = metadata.AddTypeDefinition(visibility, metadata.GetOrAddString("Boundary"),
                metadata.GetOrAddString("Type" + index), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            if (nested && index > 0)
            {
                metadata.AddNestedType(handle, parent);
            }
            parent = handle;
        }

        var image = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder());
        var bytes = new BlobBuilder();
        image.Serialize(bytes);
        return new MemoryStream(bytes.ToArray());
    }
}
