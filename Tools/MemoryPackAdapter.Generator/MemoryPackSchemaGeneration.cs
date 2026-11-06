using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using ThirdPerson.ValueGeneration;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        static string SchemaKey(ITypeSymbol type, string purpose) => Identity(type) + "|" + purpose;

        static Dictionary<string, HashSet<string>> ReadExternalSchemas(Compilation compilation)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
                foreach (AttributeData attribute in assembly.GetAttributes().Where(attribute =>
                    Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueSerializationSchemaAttribute"))
                {
                    if (!(attribute.ConstructorArguments[0].Value is ITypeSymbol type) || type.TypeKind == TypeKind.Error)
                        continue;
                    string key = SchemaKey(type, (string)attribute.ConstructorArguments[1].Value);
                    if (!result.TryGetValue(key, out HashSet<string> hashes))
                        result.Add(key, hashes = new HashSet<string>(StringComparer.Ordinal));
                    hashes.Add((string)attribute.ConstructorArguments[2].Value);
                }
            return result;
        }

        sealed class MemoryPackSchemas
        {
            readonly string m_Library;
            readonly IDictionary<string, LogicalStoragePlan> m_LogicalStorages;
            readonly Func<ITypeSymbol, ITypeSymbol, bool> m_ImplicitReference;
            readonly string m_StringFormat;
            readonly IDictionary<string, StorageProjection> m_Projections;
            readonly IDictionary<string, string> m_Purposes;
            readonly IDictionary<string, HashSet<string>> m_ExternalSchemas;
            readonly ISet<string> m_LocalTypes;
            readonly Dictionary<string, string> m_Hashes = new Dictionary<string, string>(StringComparer.Ordinal);

            internal MemoryPackSchemas(Compilation compilation, IDictionary<string, LogicalStoragePlan> logicalStorages,
                Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, string stringFormat,
                IDictionary<string, StorageProjection> projections, IDictionary<string, string> purposes,
                IDictionary<string, HashSet<string>> externalSchemas, ISet<string> localTypes)
            {
                m_Library = compilation.GetTypeByMetadataName("MemoryPack.MemoryPackSerializer").ContainingAssembly.Identity.ToString();
                m_LogicalStorages = logicalStorages;
                m_ImplicitReference = implicitReference;
                m_StringFormat = stringFormat;
                m_Projections = projections;
                m_Purposes = purposes;
                m_ExternalSchemas = externalSchemas;
                m_LocalTypes = localTypes;
            }

            internal string Purpose(ITypeSymbol type) => m_Purposes.TryGetValue(Identity(type), out string purpose) &&
                UsesProjection(type, purpose, m_ImplicitReference) ? purpose : null;

            internal string Hash(ITypeSymbol type)
            {
                string key = Identity(type);
                if (!m_Hashes.TryGetValue(key, out string hash))
                {
                    hash = Build((code, visited) => AppendType(code, type, visited, Purpose(type), true));
                    m_Hashes.Add(key, hash);
                }
                return hash;
            }

            string Build(Action<StringBuilder, HashSet<string>> append)
            {
                var code = new StringBuilder("thirdperson.memorypack.schema/1\n").Append(m_Library).Append('\n');
                append(code, new HashSet<string>(StringComparer.Ordinal));
                using (SHA256 hash = SHA256.Create())
                    return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(code.ToString())).Select(value =>
                        value.ToString("x2", CultureInfo.InvariantCulture)));
            }

            void AppendType(StringBuilder code, ITypeSymbol type, HashSet<string> visited, string purpose, bool root = false)
            {
                if (type is INamedTypeSymbol tuple) type = RecordType(tuple);
                if (IsFloat32Vector4(type))
                {
                    code.AppendLine("type memorypack.float32x4/sequential-xyzw/1");
                    return;
                }
                string name = TypeName(type);
                code.Append("type ").Append(name).Append('\n');
                if (!visited.Add(name)) return;
                if (purpose != null && !root)
                {
                    string childPurpose = UsesProjection(type, purpose, m_ImplicitReference) ? purpose : null;
                    if (m_LocalTypes.Contains(Identity(type)))
                    {
                        code.Append("value-schema ").Append(Hash(type)).Append('\n');
                        return;
                    }
                    if (m_ExternalSchemas.TryGetValue(SchemaKey(type, childPurpose), out HashSet<string> hashes))
                    {
                        code.Append("value-schema ").Append(hashes.Single()).Append('\n');
                        return;
                    }
                }
                if (m_LogicalStorages.TryGetValue(Identity(type), out LogicalStoragePlan logical))
                {
                    code.AppendLine("logical-storage");
                    foreach (Member entry in logical.Entries)
                    {
                        code.Append(entry.Name).Append(':').Append(HasAttribute(entry.Symbol, PresenceAttribute)).Append(':')
                            .Append(HasAttribute(entry.Symbol, PayloadAttribute)).Append('\n');
                        AppendType(code, entry.Type, visited, purpose);
                    }
                    return;
                }
                if (type.SpecialType == SpecialType.System_String && m_StringFormat != null)
                    code.Append("string-codec ").Append(m_StringFormat).Append('\n');
                if (type.SpecialType != SpecialType.None) return;
                if (NullableElement(type) is ITypeSymbol nullable)
                {
                    code.AppendLine("nullable-object");
                    AppendType(code, nullable, visited, purpose);
                    return;
                }
                if (SequenceElement(type) is ITypeSymbol element)
                {
                    code.AppendLine("collection");
                    AppendType(code, element, visited, purpose);
                    return;
                }
                if (!(type is INamedTypeSymbol record)) return;
                if (type.TypeKind == TypeKind.Enum)
                {
                    AppendType(code, record.EnumUnderlyingType, visited, purpose);
                    foreach (IFieldSymbol field in record.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue))
                        code.Append(field.Name).Append('=').Append(Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)).Append('\n');
                    return;
                }
                if (m_Projections.TryGetValue(Identity(type), out StorageProjection projection))
                {
                    code.Append("projection ").Append(projection.Purpose).Append(':').Append(projection.Factory.ToDisplayString()).Append('\n');
                    foreach (IParameterSymbol parameter in projection.Factory.Parameters)
                        foreach (AttributeData attribute in parameter.GetAttributes().Where(attribute =>
                            Identity(attribute.AttributeClass) == StorageSourceAttribute || Identity(attribute.AttributeClass) == StorageContextAttribute))
                            code.Append(attribute.ToString()).Append('\n');
                    foreach (Member member in projection.Members)
                    {
                        code.Append("member ").Append(member.Name).Append('\n');
                        AppendType(code, member.Type, visited, purpose);
                    }
                    foreach (ISymbol presence in ValueTypeMembers.InHierarchy(record).Where(member => HasAttribute(member, PresenceAttribute)))
                        code.Append("presence ").Append(presence.Name).Append('\n');
                    return;
                }
                if (IsUnmanagedValue(type))
                {
                    code.AppendLine("unmanaged");
                    AppendLayout(code, record);
                    foreach (IFieldSymbol field in UnmanagedSchemaFields(record))
                    {
                        code.Append(field.Name).Append('\n');
                        AppendLayout(code, field);
                        AppendType(code, field.Type, visited, purpose);
                    }
                    return;
                }
                Variant[] variants = KnownTypes(record);
                foreach (Variant variant in variants)
                {
                    code.Append("variant ").Append(variant.Discriminator.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    AppendType(code, variant.Type, visited, purpose);
                }
                if (variants.Length != 0) return;
                AttributeData union = record.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == UnionAttribute);
                if (union != null) AppendType(code, (ITypeSymbol)union.ConstructorArguments[0].Value, visited, purpose);
                Member[] members = ValueMembers(record, m_ImplicitReference);
                if (!HasAttribute(record, UnionTemplateAttribute) && members.Any(member => member.IsCase))
                    AppendType(code, UnionKindMember(record).Type, visited, purpose);
                foreach (Member member in members)
                {
                    code.Append("member ").Append(member.Name).Append(':').Append(member.IsCase).Append(':')
                        .Append(HasAttribute(member.Symbol, PayloadAttribute)).Append(':').Append(DeclaredText(member.Symbol)).Append('\n');
                    AppendType(code, member.Type, visited, purpose);
                }
                foreach (ISymbol presence in ValueTypeMembers.InHierarchy(record).Where(member => HasAttribute(member, PresenceAttribute)))
                    code.Append("presence ").Append(presence.Name).Append('\n');
            }

            static bool IsFloat32Vector4(ITypeSymbol type)
            {
                if (!(type is INamedTypeSymbol record) || !type.IsUnmanagedType ||
                    Identity(type) != "UnityEngine.Vector4" && Identity(type) != "System.Numerics.Vector4")
                    return false;
                IFieldSymbol[] fields = UnmanagedSchemaFields(record);
                if (fields.Length != 4) return false;
                for (int i = 0; i < fields.Length; i++)
                    if (fields[i].Type.SpecialType != SpecialType.System_Single ||
                        !string.Equals(fields[i].Name, "xyzw"[i].ToString(), StringComparison.OrdinalIgnoreCase) ||
                        fields[i].GetAttributes().Any(attribute => Identity(attribute.AttributeClass) ==
                            "System.Runtime.InteropServices.FieldOffsetAttribute"))
                        return false;
                return !record.GetAttributes().Any(attribute => Identity(attribute.AttributeClass) ==
                    "System.Runtime.InteropServices.StructLayoutAttribute" &&
                    ((int)attribute.ConstructorArguments[0].Value != 0 || attribute.NamedArguments.Any(argument =>
                        argument.Key == "Size" && (int)argument.Value.Value != 0 && (int)argument.Value.Value != 16)));
            }

            static IFieldSymbol[] UnmanagedSchemaFields(INamedTypeSymbol record)
            {
                IFieldSymbol[] fields = record.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
                string name = Identity(record);
                if (name == "System.Numerics.Quaternion" || name == "System.Numerics.Vector4" || name == "UnityEngine.Vector4")
                    Array.Sort(fields, (left, right) =>
                        "xyzw".IndexOf(left.Name, StringComparison.OrdinalIgnoreCase).CompareTo(
                            "xyzw".IndexOf(right.Name, StringComparison.OrdinalIgnoreCase)));
                return fields;
            }

            static void AppendLayout(StringBuilder code, ISymbol symbol)
            {
                foreach (AttributeData attribute in symbol.GetAttributes().Where(attribute =>
                    Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.StructLayoutAttribute" ||
                    Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.FieldOffsetAttribute" ||
                    Identity(attribute.AttributeClass) == "System.Runtime.CompilerServices.FixedBufferAttribute"))
                    code.Append(attribute.ToString()).Append('\n');
            }
        }

        static void EmitMemoryPackFormatterHeader(StringBuilder code, string formatter, string type, string schemaHash,
            string preparedStorage = null)
        {
            code.Append("sealed class ").Append(formatter).Append(" : global::MemoryPack.MemoryPackFormatter<")
                .Append(type).Append(">, global::ThirdPersonSimulation.Serialization.IMemoryPackSchema");
            if (preparedStorage != null)
                code.Append(", global::ThirdPersonSimulation.Serialization.IMemoryPackPreparedFormatter<")
                    .Append(type).Append(", ").Append(preparedStorage).Append('>');
            code.AppendLine(" {");
            code.Append("static readonly global::ThirdPersonSimulation.StableHash s_SchemaHash = new global::ThirdPersonSimulation.StableHash(\"")
                .Append(schemaHash).AppendLine("\");");
            code.AppendLine("public global::ThirdPersonSimulation.StableHash SchemaHash => s_SchemaHash;");
        }
    }
}
