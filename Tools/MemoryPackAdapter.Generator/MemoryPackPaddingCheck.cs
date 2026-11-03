using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        // MemoryPack 按原始内存写出非托管值；隐式填充会把未初始化字节带进产物与内容身份。
        // Roslyn 不为引用程序集的类型返回 StructLayout 伪特性，此时按默认顺序布局计算。
        static string PaddingReason(ITypeSymbol type)
        {
            if (!Layout(type, out _, out _, out int padding) || padding == 0) return null;
            return $"以原始内存写出的非托管值含 {padding} 个隐式填充字节，同一数据可能写出不同字节；请放宽字段填满对齐空隙（引用程序集上的 StructLayout 对生成器与 schema Hash 不可见）";
        }

        static bool Layout(ITypeSymbol type, out int size, out int alignment, out int padding)
        {
            padding = 0;
            if (type.TypeKind == TypeKind.Enum)
                return Layout(((INamedTypeSymbol)type).EnumUnderlyingType, out size, out alignment, out padding);
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: case SpecialType.System_Byte: case SpecialType.System_SByte:
                    size = alignment = 1; return true;
                case SpecialType.System_Char: case SpecialType.System_Int16: case SpecialType.System_UInt16:
                    size = alignment = 2; return true;
                case SpecialType.System_Int32: case SpecialType.System_UInt32: case SpecialType.System_Single:
                    size = alignment = 4; return true;
                case SpecialType.System_Int64: case SpecialType.System_UInt64: case SpecialType.System_Double:
                case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr:
                    size = alignment = 8; return true;
                case SpecialType.System_Decimal:
                    size = 16; alignment = 8; return true;
            }
            size = alignment = 0;
            if (!(type is INamedTypeSymbol record) || record.TypeKind != TypeKind.Struct) return false;
            AttributeData layout = record.GetAttributes().SingleOrDefault(attribute =>
                Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.StructLayoutAttribute");
            int kind = layout == null ? 0 : Convert.ToInt32(layout.ConstructorArguments[0].Value);
            int pack = 0, minimumSize = 0;
            if (layout != null)
                foreach (var argument in layout.NamedArguments)
                    if (argument.Key == "Pack") pack = Convert.ToInt32(argument.Value.Value);
                    else if (argument.Key == "Size") minimumSize = Convert.ToInt32(argument.Value.Value);
            IFieldSymbol[] fields = record.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
            var covered = new bool[0];
            int offset = 0;
            alignment = 1;
            foreach (IFieldSymbol field in fields)
            {
                int fieldSize, fieldAlignment;
                if (field.IsFixedSizeBuffer)
                {
                    AttributeData buffer = field.GetAttributes().SingleOrDefault(attribute =>
                        Identity(attribute.AttributeClass) == "System.Runtime.CompilerServices.FixedBufferAttribute");
                    if (buffer == null ||
                        !Layout(((IPointerTypeSymbol)field.Type).PointedAtType, out int element, out fieldAlignment, out _))
                        return false;
                    fieldSize = element * Convert.ToInt32(buffer.ConstructorArguments[1].Value);
                }
                else if (!Layout(field.Type, out fieldSize, out fieldAlignment, out int inner)) return false;
                else padding += inner;
                if (pack > 0) fieldAlignment = Math.Min(fieldAlignment, pack);
                alignment = Math.Max(alignment, fieldAlignment);
                int start;
                if (kind == 2)
                {
                    AttributeData explicitOffset = field.GetAttributes().SingleOrDefault(attribute =>
                        Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.FieldOffsetAttribute");
                    if (explicitOffset == null) return false;
                    start = Convert.ToInt32(explicitOffset.ConstructorArguments[0].Value);
                }
                else start = (offset + fieldAlignment - 1) / fieldAlignment * fieldAlignment;
                offset = Math.Max(offset, start + fieldSize);
                if (covered.Length < offset) Array.Resize(ref covered, offset);
                for (int i = start; i < start + fieldSize; i++) covered[i] = true;
            }
            size = Math.Max(Math.Max(offset, minimumSize), 1);
            size = (size + alignment - 1) / alignment * alignment;
            if (fields.Length == 0) return true;
            Array.Resize(ref covered, size);
            padding += covered.Count(value => !value);
            return true;
        }
    }
}
