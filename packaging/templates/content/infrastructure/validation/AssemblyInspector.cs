using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Ngb.Validation;

public sealed record PublicContract(string Type, string Kind, string Name, string Signature);

public sealed record AssemblyContract(string Name, string Version, string Sha256,
    IReadOnlyList<string> References, IReadOnlyList<PublicContract> PublicContracts);

public static class AssemblyInspector
{
    public static AssemblyContract Inspect(Stream stream)
    {
        if (!stream.CanSeek || stream.Length > 64 * 1024 * 1024)
        {
            throw new InvalidDataException("Assembly must be seekable and no larger than 64 MiB.");
        }

        stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Position = 0;
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!pe.HasMetadata)
        {
            throw new InvalidDataException("The file has no managed metadata.");
        }

        var metadata = pe.GetMetadataReader();
        if (metadata.TypeDefinitions.Count > 100000)
        {
            throw new InvalidDataException("Assembly type inventory exceeds the inspection limit.");
        }
        var definition = metadata.GetAssemblyDefinition();
        var contracts = new List<PublicContract>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!IsPublic(metadata, handle))
            {
                continue;
            }

            var name = QualifiedName(metadata, handle);
            contracts.Add(new PublicContract(name, "type", name, type.Attributes.ToString()));
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                var visibility = method.Attributes & MethodAttributes.MemberAccessMask;
                if (visibility is not (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem))
                {
                    continue;
                }

                contracts.Add(new PublicContract(name, "method", metadata.GetString(method.Name),
                    Convert.ToHexStringLower(metadata.GetBlobBytes(method.Signature))));
            }

            foreach (var fieldHandle in type.GetFields())
            {
                var field = metadata.GetFieldDefinition(fieldHandle);
                var visibility = field.Attributes & FieldAttributes.FieldAccessMask;
                if (visibility is not (FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem))
                {
                    continue;
                }

                contracts.Add(new PublicContract(name, "field", metadata.GetString(field.Name),
                    Convert.ToHexStringLower(metadata.GetBlobBytes(field.Signature))));
            }
        }

        var references = metadata.AssemblyReferences.Select(handle =>
            metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Order(StringComparer.Ordinal).ToArray();

        return new AssemblyContract(metadata.GetString(definition.Name), definition.Version.ToString(), hash,
            references, contracts.OrderBy(contract => contract.Type, StringComparer.Ordinal)
                .ThenBy(contract => contract.Kind, StringComparer.Ordinal)
                .ThenBy(contract => contract.Name, StringComparer.Ordinal)
                .ThenBy(contract => contract.Signature, StringComparer.Ordinal).ToArray());
    }

    private static bool IsPublic(MetadataReader metadata, TypeDefinitionHandle handle, int depth = 0)
    {
        if (depth > 32)
        {
            throw new InvalidDataException("Nested type metadata exceeds the inspection limit.");
        }

        var type = metadata.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        if (visibility == TypeAttributes.Public)
        {
            return true;
        }

        return visibility is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem
            && IsPublic(metadata, type.GetDeclaringType(), depth + 1);
    }

    private static string QualifiedName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        var name = metadata.GetString(type.Name);
        if (!parent.IsNil)
        {
            return QualifiedName(metadata, parent) + "+" + name;
        }

        var space = metadata.GetString(type.Namespace);
        return string.IsNullOrEmpty(space) ? name : space + "." + name;
    }
}
