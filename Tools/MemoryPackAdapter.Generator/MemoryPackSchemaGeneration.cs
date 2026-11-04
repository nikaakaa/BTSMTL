using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        sealed class MemoryPackSchemas
        {
            readonly string m_Library;
            readonly IDictionary<string, LogicalStoragePlan> m_LogicalStorages;
            readonly Func<ITypeSymbol, ITypeSymbol, bool> m_ImplicitReference;
            readonly string m_StringFormat;
            readonly IDictionary<string, StorageProjection> m_Projections;

            internal MemoryPackSchemas(Compilation compilation, IDictionary<string, LogicalStoragePlan> logicalStorages,
                Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, string stringFormat,
                IDictionary<string, StorageProjection> projections)
            {
                m_Library = compilation.GetTypeByMetadataName("MemoryPack.MemoryPackSerializer").ContainingAssembly.Identity.ToString();
                m_LogicalStorages = logicalStorages;
                m_ImplicitReference = implicitReference;
                m_StringFormat = stringFormat;
                m_Projections = projections;
            }

            internal string Hash(ITypeSymbol type) => Build((code, visited) => AppendType(code, type, visited));

            string Build(Action<StringBuilder, HashSet<string>> append)
            {
                var code = new StringBuilder("thirdperson.memorypack.schema/1\n").Append(m_Library).Append('\n');
                append(code, new HashSet<string>(StringComparer.Ordinal));
                using (SHA256 hash = SHA256.Create())
                    return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(code.ToString())).Select(value =>
                        value.ToString("x2", CultureInfo.InvariantCulture)));
            }

            void AppendType(StringBuilder code, ITypeSymbol type, HashSet<string> visited)
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
                if (m_LogicalStorages.TryGetValue(Identity(type), out LogicalStoragePlan logical))
                {
                    code.AppendLine("logical-storage");
                    foreach (Member entry in logical.Entries)
                    {
                        code.Append(entry.Name).Append(':').Append(HasAttribute(entry.Symbol, PresenceAttribute)).Append(':')
                            .Append(HasAttribute(entry.Symbol, PayloadAttribute)).Append('\n');
                        AppendType(code, entry.Type, visited);
                    }
                    return;
                }
                if (type.SpecialType == SpecialType.System_String && m_StringFormat != null)
                    code.Append("string-codec ").Append(m_StringFormat).Append('\n');
                if (type.SpecialType != SpecialType.None) return;
                if (NullableElement(type) is ITypeSymbol nullable)
                {
                    code.AppendLine("nullable-object");
                    AppendType(code, nullable, visited);
                    return;
                }
                if (SequenceElement(type) is ITypeSymbol element)
                {
                    code.AppendLine("collection");
                    AppendType(code, element, visited);
                    return;
                }
                if (!(type is INamedTypeSymbol record)) return;
                if (type.TypeKind == TypeKind.Enum)
                {
                    AppendType(code, record.EnumUnderlyingType, visited);
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
                        AppendType(code, member.Type, visited);
                    }
                    foreach (ISymbol presence in record.GetMembers().Where(member => HasAttribute(member, PresenceAttribute)))
                        code.Append("presence ").Append(presence.Name).Append('\n');
                    return;
                }
                if (IsUnmanagedValue(type))
                {
                    code.AppendLine("unmanaged");
                    AppendLayout(code, record);
                    foreach (IFieldSymbol field in record.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                    {
                        code.Append(field.Name).Append('\n');
                        AppendLayout(code, field);
                        AppendType(code, field.Type, visited);
                    }
                    return;
                }
                Variant[] variants = KnownTypes(record);
                foreach (Variant variant in variants)
                {
                    code.Append("variant ").Append(variant.Discriminator.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    AppendType(code, variant.Type, visited);
                }
                if (variants.Length != 0) return;
                AttributeData union = record.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == UnionAttribute);
                if (union != null) AppendType(code, (ITypeSymbol)union.ConstructorArguments[0].Value, visited);
                Member[] members = ValueMembers(record, m_ImplicitReference);
                if (!HasAttribute(record, UnionTemplateAttribute) && members.Any(member => member.IsCase))
                    AppendType(code, UnionKindMember(record).Type, visited);
                foreach (Member member in members)
                {
                    code.Append("member ").Append(member.Name).Append(':').Append(member.IsCase).Append(':')
                        .Append(HasAttribute(member.Symbol, PayloadAttribute)).Append(':').Append(DeclaredText(member.Symbol)).Append('\n');
                    AppendType(code, member.Type, visited);
                }
                foreach (ISymbol presence in record.GetMembers().Where(member => HasAttribute(member, PresenceAttribute)))
                    code.Append("presence ").Append(presence.Name).Append('\n');
            }

            static bool IsFloat32Vector4(ITypeSymbol type)
            {
                if (!(type is INamedTypeSymbol record) || !type.IsUnmanagedType ||
                    Identity(type) != "UnityEngine.Vector4" && Identity(type) != "System.Numerics.Vector4")
                    return false;
                IFieldSymbol[] fields = record.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
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
