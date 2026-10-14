using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;
using static ThirdPerson.ValueGeneration.ValueSnapshotModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        sealed class MemoryPackSchemas
        {
            readonly string m_Library;
            readonly IDictionary<string, NativeSnapshotPlan> m_Snapshots;
            readonly Func<ITypeSymbol, ITypeSymbol, bool> m_ImplicitReference;

            internal MemoryPackSchemas(Compilation compilation, IDictionary<string, NativeSnapshotPlan> snapshots,
                Func<ITypeSymbol, ITypeSymbol, bool> implicitReference)
            {
                m_Library = compilation.GetTypeByMetadataName("MemoryPack.MemoryPackSerializer").ContainingAssembly.Identity.ToString();
                m_Snapshots = snapshots;
                m_ImplicitReference = implicitReference;
            }

            internal string Hash(ITypeSymbol type) => Build((code, visited) => AppendType(code, type, visited));
            internal string Hash(NativeSnapshotPlan plan) => Build((code, visited) => AppendSnapshot(code, plan, visited));

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
                string name = TypeName(type);
                code.Append("type ").Append(name).Append('\n');
                if (!visited.Add(name)) return;
                if (m_Snapshots.TryGetValue(name, out NativeSnapshotPlan snapshot))
                {
                    AppendSnapshot(code, snapshot, visited);
                    return;
                }
                if (HasAttribute(type, TextAttribute)) { code.AppendLine("utf16-text"); return; }
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
                foreach (Member member in ValueMembers(record, m_ImplicitReference))
                {
                    code.Append("member ").Append(member.Name).Append(':').Append(member.IsCase).Append(':')
                        .Append(HasAttribute(member.Symbol, PayloadAttribute)).Append(':').Append(DeclaredText(member.Symbol)).Append('\n');
                    AppendType(code, member.Type, visited);
                }
                foreach (ISymbol presence in record.GetMembers().Where(member => HasAttribute(member, PresenceAttribute)))
                    code.Append("presence ").Append(presence.Name).Append('\n');
            }

            void AppendSnapshot(StringBuilder code, NativeSnapshotPlan plan, HashSet<string> visited)
            {
                code.Append("snapshot ").Append(plan.Name).Append('\n');
                if (!visited.Add("snapshot:" + plan.Name)) return;
                for (int index = 0; index < plan.Fields.Length; index++)
                {
                    code.Append(plan.Fields[index].Name).Append(':').Append(DeclaredText(plan.Fields[index])).Append('\n');
                    AppendShape(code, plan.Shapes[index], visited);
                }
            }

            void AppendShape(StringBuilder code, NativeSnapshotShape shape, HashSet<string> visited)
            {
                code.Append(shape.Kind).Append(':').Append(shape.FixedKeys).Append(':').Append(shape.DynamicSource).Append('\n');
                if (shape.Kind == NativeSnapshotKind.Source && !shape.DynamicSource &&
                    m_Snapshots.TryGetValue(!IsSnapshotCopy(shape.Type) && HasAttribute(shape.Type, SnapshotAttributeName)
                        ? SnapshotTypeName((INamedTypeSymbol)shape.Type) : TypeName(shape.Type), out NativeSnapshotPlan nested))
                    AppendSnapshot(code, nested, visited);
                else if (shape.Kind == NativeSnapshotKind.Value || shape.Kind == NativeSnapshotKind.Source)
                    AppendType(code, shape.Type, visited);
                if (shape.Key != null) AppendShape(code, shape.Key, visited);
                if (shape.Element != null) AppendShape(code, shape.Element, visited);
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
