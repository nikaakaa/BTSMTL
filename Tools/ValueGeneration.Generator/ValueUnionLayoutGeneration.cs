using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        sealed class ValueUnionLayout
        {
            internal int PayloadOffset;
            internal int Size;
            internal int Alignment;
        }

        static int RequestedValueUnionLayout(INamedTypeSymbol type) => type.GetAttributes()
            .Where(attribute => Identity(attribute.AttributeClass) == UnionAttribute)
            .SelectMany(attribute => attribute.NamedArguments).Where(argument => argument.Key == "Layout")
            .Select(argument => Convert.ToInt32(argument.Value.Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(3).Single();

        static bool TryExplicitValueUnionLayout(CSharpCompilation compilation, INamedTypeSymbol type,
            out ValueUnionLayout layout, out string reason)
        {
            layout = null;
            reason = null;
            int kind = RequestedValueUnionLayout(type);
            if (kind == 3)
                return true;
            if (kind != 2)
            {
                reason = "ValueUnion 的生成布局只接受 Auto 或 Explicit";
                return false;
            }
            compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
            SyntaxNode declaration = type.DeclaringSyntaxReferences[0].GetSyntax();
            type = (INamedTypeSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);
            return new ValueTypeLayout(compilation).TryUnion(type, GetValueUnionShape(type), out layout, out reason);
        }

        sealed class ValueTypeLayout
        {
            readonly CSharpCompilation m_Compilation;
            readonly StateLayoutSchema m_Metadata;
            readonly Dictionary<SyntaxTree, int> m_SourceOrder;
            readonly ImmutableDictionary<ISymbol, ValueTypeSize>.Builder m_Layouts =
                ImmutableDictionary.CreateBuilder<ISymbol, ValueTypeSize>(SymbolEqualityComparer.Default);

            internal ValueTypeLayout(CSharpCompilation compilation)
            {
                m_Compilation = compilation;
                m_Metadata = new StateLayoutSchema(compilation);
                m_SourceOrder = compilation.SyntaxTrees.Select((tree, index) => new { tree, index })
                    .ToDictionary(source => source.tree, source => source.index);
            }

            internal bool TryUnion(INamedTypeSymbol type, ValueUnionShape union, out ValueUnionLayout layout, out string reason)
            {
                layout = null;
                reason = null;
                if (type.TypeKind != TypeKind.Struct || ContainsStateTypeParameter(type) || type.Arity != 0 ||
                    type.ContainingType != null || union == null || union.Definition == null || union.KindMember != null ||
                    union.Common.Length != 0 || union.Cases.Any(member => !member.Generated) ||
                    type.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic) || HasAttribute(type, StructLayoutAttribute))
                {
                    reason = "Explicit ValueUnion 必须在空的非泛型顶层 struct 上引用闭合联合声明，Kind、实例字段及布局由生成器提供";
                    return false;
                }
                ITypeSymbol kind = union.KindOwner.TypeKind == TypeKind.Enum ? union.KindOwner.EnumUnderlyingType :
                    m_Compilation.GetSpecialType(Cases(UnionMembers(union.KindOwner.OriginalDefinition)).Length <= byte.MaxValue
                        ? SpecialType.System_Byte : SpecialType.System_Int32);
                if (!TryGet(kind, out ValueTypeSize discriminator, out reason))
                    return false;
                int payloadSize = 0;
                int payloadAlignment = 1;
                foreach (Member member in union.Cases)
                {
                    if (!TryGet(member.Type, out ValueTypeSize branch, out reason))
                    {
                        reason = member.Symbol.Name + " -> " + reason;
                        return false;
                    }
                    payloadSize = Math.Max(payloadSize, branch.Size);
                    payloadAlignment = Math.Max(payloadAlignment, branch.Alignment);
                }
                int offset = AlignUp(discriminator.Size, payloadAlignment);
                int alignment = Math.Max(discriminator.Alignment, payloadAlignment);
                layout = new ValueUnionLayout
                {
                    PayloadOffset = offset,
                    Size = AlignUp(offset + payloadSize, alignment),
                    Alignment = alignment
                };
                return true;
            }

            bool TryGet(ITypeSymbol type, out ValueTypeSize layout, out string reason)
            {
                reason = null;
                if (m_Layouts.TryGetValue(type, out layout))
                    return true;
                int primitive = PrimitiveSize(type.SpecialType);
                if (primitive != 0)
                {
                    layout = new ValueTypeSize { Size = primitive, Alignment = primitive };
                    m_Layouts.Add(type, layout);
                    return true;
                }
                if (type is INamedTypeSymbol enumeration && enumeration.TypeKind == TypeKind.Enum)
                    return TryGet(enumeration.EnumUnderlyingType, out layout, out reason);
                if (!(type is INamedTypeSymbol named) || named.TypeKind != TypeKind.Struct ||
                    named.IsRefLikeType || ContainsStateTypeParameter(named) || !IsStateLayoutUnmanaged(named) ||
                    named.SpecialType == SpecialType.System_IntPtr || named.SpecialType == SpecialType.System_UIntPtr)
                {
                    reason = Identity(type) + ": 联合载荷必须具有不含引用或平台指针的闭合 CLR 值布局";
                    return false;
                }
                if (named.DeclaringSyntaxReferences.Length != 0 && RequestedValueUnionLayout(named) == 2)
                {
                    if (!TryUnion(named, GetValueUnionShape(named), out ValueUnionLayout union, out reason))
                        return false;
                    layout = new ValueTypeSize { Size = union.Size, Alignment = union.Alignment };
                    m_Layouts.Add(type, layout);
                    return true;
                }
                if (!TryFields(named, out int kind, out int pack, out int declaredSize, out LayoutField[] fields, out reason))
                    return false;
                if (kind != 0 && kind != 2)
                {
                    reason = Identity(type) + ": Auto 布局不能决定 Explicit 联合的字段偏移";
                    return false;
                }
                int size = 0;
                int alignment = 1;
                foreach (LayoutField field in fields)
                {
                    if (!TryField(field.Symbol, out ValueTypeSize member, out reason))
                        return false;
                    int fieldAlignment = pack == 0 ? member.Alignment : Math.Min(pack, member.Alignment);
                    alignment = Math.Max(alignment, fieldAlignment);
                    int offset = kind == 2 ? field.Offset : AlignUp(size, fieldAlignment);
                    if (offset < 0)
                    {
                        reason = Identity(field.Symbol) + ": Explicit 成员缺少正式 FieldOffset";
                        return false;
                    }
                    size = Math.Max(size, offset + member.Size);
                }
                layout = new ValueTypeSize
                {
                    Size = Math.Max(1, declaredSize == 0 ? AlignUp(size, alignment) : Math.Max(size, declaredSize)),
                    Alignment = alignment
                };
                m_Layouts.Add(type, layout);
                return true;
            }

            bool TryFields(INamedTypeSymbol type, out int kind, out int pack, out int size,
                out LayoutField[] fields, out string reason)
            {
                reason = null;
                fields = null;
                kind = pack = size = 0;
                if (type.DeclaringSyntaxReferences.Length != 0)
                {
                    AttributeData layout = type.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == StructLayoutAttribute);
                    kind = layout == null ? 0 : Convert.ToInt32(layout.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                    pack = StateLayoutSchema.LayoutArgument(layout, "Pack", 0);
                    size = StateLayoutSchema.LayoutArgument(layout, "Size", 0);
                    fields = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic)
                        .OrderBy(field => m_SourceOrder[(field.AssociatedSymbol ?? field).Locations[0].SourceTree])
                        .ThenBy(field => (field.AssociatedSymbol ?? field).Locations[0].SourceSpan.Start).Select(field =>
                    {
                        AttributeData offset = field.GetAttributes().SingleOrDefault(attribute =>
                            Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.FieldOffsetAttribute");
                        return new LayoutField { Symbol = field, Offset = offset == null ? -1 : (int)offset.ConstructorArguments[0].Value };
                    }).ToArray();
                    return true;
                }
                if (!m_Metadata.TryMetadataType(type, out StateLayoutSchema.MetadataStateType metadata))
                {
                    reason = Identity(type) + ": 无法读取引用类型的正式 CLR 布局";
                    return false;
                }
                kind = metadata.Kind;
                pack = metadata.Pack;
                size = metadata.Size;
                var members = new List<LayoutField>();
                foreach (StateLayoutSchema.MetadataStateField field in metadata.Fields)
                {
                    IFieldSymbol symbol = type.GetMembers(field.Name).OfType<IFieldSymbol>().SingleOrDefault();
                    if (symbol == null)
                    {
                        reason = Identity(type) + "." + field.Name + ": 无法读取引用类型的原实例字段";
                        return false;
                    }
                    members.Add(new LayoutField { Symbol = symbol, Offset = field.Offset });
                }
                fields = members.ToArray();
                return true;
            }

            bool TryField(IFieldSymbol field, out ValueTypeSize layout, out string reason)
            {
                AttributeData fixedBuffer = field.GetAttributes().SingleOrDefault(attribute =>
                    Identity(attribute.AttributeClass) == "System.Runtime.CompilerServices.FixedBufferAttribute");
                ITypeSymbol element = null;
                int count = 1;
                if (fixedBuffer != null)
                {
                    element = (ITypeSymbol)fixedBuffer.ConstructorArguments[0].Value;
                    count = (int)fixedBuffer.ConstructorArguments[1].Value;
                }
                else if (field.IsFixedSizeBuffer)
                {
                    layout = null;
                    VariableDeclaratorSyntax declaration = field.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>().SingleOrDefault();
                    if (declaration?.ArgumentList?.Arguments.Count != 1)
                    {
                        reason = Identity(field) + ": 无法读取 fixed 载荷的正式长度";
                        return false;
                    }
                    Optional<object> length = m_Compilation.GetSemanticModel(declaration.SyntaxTree)
                        .GetConstantValue(declaration.ArgumentList.Arguments[0].Expression);
                    if (!length.HasValue)
                    {
                        reason = Identity(field) + ": fixed 载荷长度必须是常量";
                        return false;
                    }
                    element = ((IPointerTypeSymbol)field.Type).PointedAtType;
                    count = Convert.ToInt32(length.Value, CultureInfo.InvariantCulture);
                }
                if (!TryGet(element ?? field.Type, out ValueTypeSize member, out reason))
                {
                    layout = null;
                    return false;
                }
                layout = new ValueTypeSize { Size = member.Size * count, Alignment = member.Alignment };
                return true;
            }

            static int PrimitiveSize(SpecialType type)
            {
                switch (type)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Byte:
                    case SpecialType.System_SByte: return 1;
                    case SpecialType.System_Char:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16: return 2;
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_Single: return 4;
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                    case SpecialType.System_Double: return 8;
                    default: return 0;
                }
            }

            internal static int AlignUp(int size, int alignment) => (size + alignment - 1) / alignment * alignment;

            sealed class ValueTypeSize
            {
                internal int Size;
                internal int Alignment;
            }

            sealed class LayoutField
            {
                internal IFieldSymbol Symbol;
                internal int Offset;
            }
        }
    }
}
