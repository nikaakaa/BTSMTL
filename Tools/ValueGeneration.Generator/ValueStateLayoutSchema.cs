using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        sealed class StateLayoutSchema
        {
            readonly CSharpCompilation m_Compilation;
            readonly Dictionary<AssemblyIdentity, Dictionary<string, MetadataStateType>> m_Metadata =
                new Dictionary<AssemblyIdentity, Dictionary<string, MetadataStateType>>();

            internal StateLayoutSchema(CSharpCompilation compilation) => m_Compilation = compilation;

            internal bool Append(StringBuilder schema, ITypeSymbol type, out string reason)
            {
                reason = null;
                StateSchemaPart(schema, Identity(type));
                if (type is IPointerTypeSymbol || type.SpecialType == SpecialType.System_IntPtr ||
                    type.SpecialType == SpecialType.System_UIntPtr)
                {
                    reason = Identity(type) + ": 编排区段不能保存原生指针";
                    return false;
                }
                if (IsStatePrimitive(type.SpecialType))
                    return true;
                if (!(type is INamedTypeSymbol named) || named.IsRefLikeType ||
                    !IsStateLayoutUnmanaged(named) || ContainsStateTypeParameter(named))
                {
                    reason = Identity(type) + ": 无法从闭合原成员确定 unmanaged 布局";
                    return false;
                }
                if (named.TypeKind == TypeKind.Enum)
                {
                    foreach (IFieldSymbol value in named.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue))
                    {
                        StateSchemaPart(schema, value.Name);
                        StateSchemaPart(schema, Convert.ToString(value.ConstantValue, CultureInfo.InvariantCulture));
                    }
                    return Append(schema, named.EnumUnderlyingType, out reason);
                }
                if (named.DeclaringSyntaxReferences.Length != 0)
                {
                    ValueUnionShape union = GetValueUnionShape(named);
                    if (union != null && !ValidateUnionLayout(named, union, out reason))
                        return false;
                    if (union != null && RequestedValueUnionLayout(named) == LayoutKind.Explicit)
                    {
                        if (!AppendLayout(schema, named, LayoutKind.Explicit, 0, 0, (int)CharSet.Ansi, out reason))
                            return false;
                        foreach (Member member in union.Members)
                        {
                            StateSchemaPart(schema, UnionPayloadFieldName(member));
                            StateSchemaPart(schema, ValueUnionPayloadOffset.ToString(CultureInfo.InvariantCulture));
                            StateSchemaPart(schema, "1");
                            if (!Append(schema, member.Type, out reason))
                                return false;
                        }
                        if (!AppendUnionKindField(schema, "<Kind>k__BackingField", 0, union, out reason))
                            return false;
                        StateSchemaPart(schema, "end");
                        return true;
                    }
                    AttributeData layout = named.GetAttributes().SingleOrDefault(attribute =>
                        Identity(attribute.AttributeClass) == StructLayoutAttribute);
                    LayoutKind kind = layout == null ? union == null ? LayoutKind.Sequential : LayoutKind.Auto :
                        (LayoutKind)Convert.ToInt32(layout.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                    if (!AppendLayout(schema, named, kind, LayoutArgument(layout, "Pack", 0),
                        LayoutArgument(layout, "Size", 0), LayoutArgument(layout, "CharSet", (int)CharSet.Ansi), out reason))
                        return false;
                    IFieldSymbol[] fields = named.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
                    foreach (IFieldSymbol field in fields)
                    {
                        AttributeData offset = field.GetAttributes().SingleOrDefault(attribute =>
                            Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.FieldOffsetAttribute");
                        int position = offset == null ? -1 : (int)offset.ConstructorArguments[0].Value;
                        if (union != null && IsUnionKindField(field, union))
                        {
                            if (!AppendUnionKindField(schema, field.MetadataName, position, union, out reason))
                                return false;
                        }
                        else if (!AppendField(schema, field, position, out reason))
                            return false;
                    }
                    if (union != null)
                    {
                        foreach (Member member in union.Members.Where(member => member.Generated))
                        {
                            StateSchemaPart(schema, "<" + member.Symbol.Name + ">k__BackingField");
                            StateSchemaPart(schema, "-1");
                            StateSchemaPart(schema, "1");
                            if (!Append(schema, member.Type, out reason))
                                return false;
                        }
                        if (union.KindMember == null && !AppendUnionKindField(schema, "<Kind>k__BackingField", -1, union, out reason))
                            return false;
                    }
                    StateSchemaPart(schema, "end");
                    return true;
                }
                if (!TryMetadataType(named, out MetadataStateType metadata))
                {
                    reason = Identity(named) + ": 引用程序集没有可读取的原类型元数据";
                    return false;
                }
                if (!AppendLayout(schema, named, metadata.Kind, metadata.Pack, metadata.Size, metadata.CharSet, out reason))
                    return false;
                foreach (MetadataStateField field in metadata.Fields)
                {
                    IFieldSymbol original = named.GetMembers(field.Name).OfType<IFieldSymbol>().SingleOrDefault();
                    if (original == null)
                    {
                        reason = Identity(named) + "." + field.Name + ": 未能读取引用类型的原实例成员";
                        return false;
                    }
                    if (!AppendField(schema, original, field.Offset, out reason))
                        return false;
                }
                StateSchemaPart(schema, "end");
                return true;
            }

            bool AppendUnionKindField(StringBuilder schema, string name, int offset, ValueUnionShape union, out string reason)
            {
                StateSchemaPart(schema, name);
                StateSchemaPart(schema, offset.ToString(CultureInfo.InvariantCulture));
                StateSchemaPart(schema, "1");
                if (union.KindOwner.TypeKind == TypeKind.Enum)
                    return Append(schema, union.KindOwner, out reason);
                StateSchemaPart(schema, union.KindIdentity);
                Member[] cases = Cases(UnionMembers(union.KindOwner.OriginalDefinition));
                for (int index = 0; index < cases.Length; index++)
                {
                    StateSchemaPart(schema, cases[index].Symbol.Name);
                    StateSchemaPart(schema, (index + 1).ToString(CultureInfo.InvariantCulture));
                }
                return Append(schema, m_Compilation.GetSpecialType(cases.Length <= byte.MaxValue ?
                    SpecialType.System_Byte : SpecialType.System_Int32), out reason);
            }

            bool AppendField(StringBuilder schema, IFieldSymbol field, int offset, out string reason)
            {
                StateSchemaPart(schema, field.MetadataName);
                StateSchemaPart(schema, offset.ToString(CultureInfo.InvariantCulture));
                AttributeData fixedBuffer = field.GetAttributes().SingleOrDefault(attribute =>
                    Identity(attribute.AttributeClass) == "System.Runtime.CompilerServices.FixedBufferAttribute");
                if (fixedBuffer != null)
                {
                    StateSchemaPart(schema, ((int)fixedBuffer.ConstructorArguments[1].Value).ToString(CultureInfo.InvariantCulture));
                    return Append(schema, (ITypeSymbol)fixedBuffer.ConstructorArguments[0].Value, out reason);
                }
                if (field.IsFixedSizeBuffer)
                {
                    VariableDeclaratorSyntax declaration = field.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>().SingleOrDefault();
                    if (declaration?.ArgumentList?.Arguments.Count != 1)
                    {
                        reason = Identity(field) + ": 无法从原 fixed 成员读取固定长度";
                        return false;
                    }
                    Optional<object> length = m_Compilation.GetSemanticModel(declaration.SyntaxTree)
                        .GetConstantValue(declaration.ArgumentList.Arguments[0].Expression);
                    if (!length.HasValue)
                    {
                        reason = Identity(field) + ": 原 fixed 长度必须为 Int32 常量";
                        return false;
                    }
                    StateSchemaPart(schema, Convert.ToInt32(length.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return Append(schema, ((IPointerTypeSymbol)field.Type).PointedAtType, out reason);
                }
                StateSchemaPart(schema, "1");
                return Append(schema, field.Type, out reason);
            }

            internal bool TryMetadataType(INamedTypeSymbol type, out MetadataStateType metadata)
            {
                IAssemblySymbol assembly = type.ContainingAssembly;
                if (!m_Metadata.TryGetValue(assembly.Identity, out Dictionary<string, MetadataStateType> types))
                {
                    types = new Dictionary<string, MetadataStateType>(StringComparer.Ordinal);
                    foreach (PortableExecutableReference reference in m_Compilation.References.OfType<PortableExecutableReference>()
                        .Where(reference => SymbolEqualityComparer.Default.Equals(m_Compilation.GetAssemblyOrModuleSymbol(reference), assembly)))
                    {
                        Metadata value = reference.GetMetadata();
                        IEnumerable<ModuleMetadata> modules = value is AssemblyMetadata source ? source.GetModules() :
                            new[] { (ModuleMetadata)value };
                        foreach (ModuleMetadata module in modules)
                        {
                            MetadataReader reader = module.GetMetadataReader();
                            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                                types.Add(MetadataStateName(reader, handle), new MetadataStateType
                                {
                                    Reader = reader,
                                    Definition = handle
                                });
                        }
                    }
                    m_Metadata.Add(assembly.Identity, types);
                }
                if (!types.TryGetValue(StateMetadataName(type.OriginalDefinition), out metadata))
                    return false;
                if (metadata.Fields != null)
                    return true;
                TypeDefinition definition = metadata.Reader.GetTypeDefinition(metadata.Definition);
                TypeLayout layout = definition.GetLayout();
                TypeAttributes kind = definition.Attributes & TypeAttributes.LayoutMask;
                TypeAttributes charSet = definition.Attributes & TypeAttributes.StringFormatMask;
                var fields = new List<MetadataStateField>();
                foreach (FieldDefinitionHandle handle in definition.GetFields())
                {
                    FieldDefinition field = metadata.Reader.GetFieldDefinition(handle);
                    if ((field.Attributes & FieldAttributes.Static) != 0)
                        continue;
                    fields.Add(new MetadataStateField { Name = metadata.Reader.GetString(field.Name), Offset = field.GetOffset() });
                }
                metadata.Kind = kind == TypeAttributes.SequentialLayout ? LayoutKind.Sequential :
                    kind == TypeAttributes.ExplicitLayout ? LayoutKind.Explicit : LayoutKind.Auto;
                metadata.Pack = layout.PackingSize;
                metadata.Size = layout.Size;
                metadata.CharSet = (int)(charSet == TypeAttributes.UnicodeClass ? CharSet.Unicode :
                    charSet == TypeAttributes.AutoClass ? CharSet.Auto : CharSet.Ansi);
                metadata.Fields = fields.ToArray();
                return true;
            }

            static bool AppendLayout(StringBuilder schema, INamedTypeSymbol type, LayoutKind kind,
                int pack, int size, int charSet, out string reason)
            {
                reason = null;
                if (kind == LayoutKind.Auto)
                {
                    reason = Identity(type) + ": Auto 布局不能作为可按值复制的正式编排状态";
                    return false;
                }
                foreach (int value in new[] { (int)kind, pack, size, charSet })
                    StateSchemaPart(schema, value.ToString(CultureInfo.InvariantCulture));
                return true;
            }

            internal static int LayoutArgument(AttributeData layout, string name, int defaultValue) => layout == null ? defaultValue :
                layout.NamedArguments.Where(argument => argument.Key == name)
                    .Select(argument => Convert.ToInt32(argument.Value.Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(defaultValue).Single();

            static bool IsStatePrimitive(SpecialType type) => type >= SpecialType.System_Boolean && type <= SpecialType.System_Double &&
                type != SpecialType.System_Decimal;

            static string StateMetadataName(INamedTypeSymbol type) => type.ContainingType != null ?
                StateMetadataName(type.ContainingType) + "+" + type.MetadataName :
                (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

            static string MetadataStateName(MetadataReader reader, TypeDefinitionHandle handle)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                TypeDefinitionHandle owner = type.GetDeclaringType();
                string name = reader.GetString(type.Name);
                if (!owner.IsNil)
                    return MetadataStateName(reader, owner) + "+" + name;
                string names = reader.GetString(type.Namespace);
                return names.Length == 0 ? name : names + "." + name;
            }

            internal sealed class MetadataStateType
            {
                internal MetadataReader Reader;
                internal TypeDefinitionHandle Definition;
                internal LayoutKind Kind;
                internal int Pack;
                internal int Size;
                internal int CharSet;
                internal MetadataStateField[] Fields;
            }

            internal sealed class MetadataStateField
            {
                internal string Name;
                internal int Offset;
            }
        }
    }
}
